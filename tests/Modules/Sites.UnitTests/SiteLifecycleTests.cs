using SharedKernel;
using Sites.Domain;
using Xunit;

namespace Sites.UnitTests;

/// <summary>
/// Ported from <c>../backend/tests/Site.UnitTests/SiteLifecycleTests.cs</c> and extended.
///
/// <para><b>What ported and what could not.</b> The backend's four tests drive
/// <c>SiteDomainService</c> through seven NSubstitute repository mocks (<c>SiteHarness</c>). Three of
/// them — approved-site record refusal, the space-belongs-to-this-project rule, and code allocation
/// from the sequence — prove ORCHESTRATION, not entity behavior: they exist to check that a service
/// queried the right rows before mutating. This port has no repository layer to mock and no domain
/// service to drive, so those three move to <c>Sites.Application.Tests</c> at S1-07, where the same
/// questions are answered against a real database instead of against a substitute's memory. Only the
/// first test is entity behavior, and it is here, widened.</para>
///
/// <para><b><c>SiteHarness</c> itself does not port, and its cleverness does not survive the move.</b>
/// Its <c>Selects&lt;T&gt;</c> helper compiles the caller's own predicate and runs it against the
/// fixture, so a stub matched only when the service asked for that exact entity — the fix for an
/// earlier version where <c>Arg.Any</c> returned the same row for any id and "an approved site refuses
/// record writes" passed even when a different site was loaded. That is a mock-fidelity problem, and it
/// has no analogue once the handler queries a real Postgres: loading the wrong row returns the wrong
/// row. The guarantee is preserved by the architecture rather than by the test double.</para>
///
/// <para>Every failure assertion names the specific error, never a bare <c>IsFailure</c>, and also
/// asserts the entity was left unmutated — an entity that returns the right error while having already
/// changed state is still broken.</para>
/// </summary>
public class SiteLifecycleTests
{
    private static readonly DateTime SubmittedAt = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DecidedAt = new(2026, 3, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Site DraftSite(string? address = "12 Nasr Road") =>
        Site.Create(Guid.NewGuid(), "SITE-00001", Guid.NewGuid(), new SiteData(Address: address)).Value!;

    private static Site SubmittedSite()
    {
        var site = DraftSite();
        site.Submit(SubmittedAt);
        return site;
    }

    private static Site DecidedSite(bool approved, string? note = null)
    {
        var site = SubmittedSite();
        site.Decide(approved, Guid.NewGuid(), DecidedAt, note);
        return site;
    }

    // ── The legal edges ────────────────────────────────────────────────────────

    [Fact]
    public void Create_StartsInDraftWithNoStamps()
    {
        var site = DraftSite();

        Assert.Equal(SiteStatus.Draft, site.Status);
        Assert.Null(site.SubmittedAtUtc);
        Assert.Null(site.DecidedAtUtc);
        Assert.Null(site.DecidedById);
        Assert.Null(site.DecisionNote);
        Assert.True(site.IsEditable);

        // Provenance is never set through the domain -- there is deliberately no setter. See Site.cs.
        Assert.Null(site.SourceBriefId);
    }

    [Fact]
    public void Draft_Submit_Approve_IsTheHappyPath()
    {
        var site = DraftSite();

        Assert.True(site.Submit(SubmittedAt).IsSuccess);
        Assert.Equal(SiteStatus.Submitted, site.Status);
        Assert.Equal(SubmittedAt, site.SubmittedAtUtc);

        var decidedBy = Guid.NewGuid();
        Assert.True(site.Decide(approved: true, decidedBy, DecidedAt, "Measured and checked.").IsSuccess);
        Assert.Equal(SiteStatus.Approved, site.Status);
        Assert.Equal(decidedBy, site.DecidedById);
        Assert.Equal(DecidedAt, site.DecidedAtUtc);
        Assert.Equal("Measured and checked.", site.DecisionNote);
        Assert.False(site.IsEditable);
    }

    /// <summary>
    /// The Rejected half of <c>Decide</c>. Entirely untested in the backend — its four tests only ever
    /// approve, so nothing asserted that rejection reaches <see cref="SiteStatus.Rejected"/> at all, let
    /// alone that it stamps the decider and normalizes the note.
    /// </summary>
    [Fact]
    public void Decide_Rejecting_StampsTheDeciderAndKeepsTheSiteEditable()
    {
        var site = SubmittedSite();
        var decidedBy = Guid.NewGuid();

        Assert.True(site.Decide(approved: false, decidedBy, DecidedAt, "  Missing the riser shaft.  ").IsSuccess);

        Assert.Equal(SiteStatus.Rejected, site.Status);
        Assert.Equal(decidedBy, site.DecidedById);
        Assert.Equal(DecidedAt, site.DecidedAtUtc);
        Assert.Equal("Missing the riser shaft.", site.DecisionNote);

        // Rejection is the one decision that UNLOCKS a survey -- rework is the whole point of the state,
        // and it is the correction path that makes freezing Submitted affordable (B12, P5F-02).
        Assert.True(site.IsEditable);
    }

    [Fact]
    public void Decide_WithABlankNote_StoresNullRatherThanWhitespace()
    {
        var site = SubmittedSite();

        Assert.True(site.Decide(approved: true, Guid.NewGuid(), DecidedAt, "   ").IsSuccess);

        Assert.Null(site.DecisionNote);
    }

    /// <summary>
    /// <c>ReopenForRework</c>'s success path, and specifically that it clears ALL FOUR stamps. The
    /// backend tests only that reopening an APPROVED site is refused — the method's actual job, and the
    /// reason it exists (the row must satisfy its status/stamp check constraint again), had no coverage
    /// at all.
    /// </summary>
    [Fact]
    public void ReopenForRework_FromRejected_ReturnsToDraftAndClearsEveryStamp()
    {
        var site = DecidedSite(approved: false, note: "Rework the openings.");

        Assert.True(site.ReopenForRework().IsSuccess);

        Assert.Equal(SiteStatus.Draft, site.Status);
        Assert.Null(site.SubmittedAtUtc);
        Assert.Null(site.DecidedAtUtc);
        Assert.Null(site.DecidedById);
        Assert.Null(site.DecisionNote);
    }

    [Fact]
    public void ReopenForRework_ThenResubmit_IsAllowed()
    {
        var site = DecidedSite(approved: false);
        site.ReopenForRework();

        Assert.True(site.Submit(SubmittedAt).IsSuccess);
        Assert.Equal(SiteStatus.Submitted, site.Status);
    }

    // ── The illegal edges ──────────────────────────────────────────────────────

    // The status theories take the enum's underlying int, not the enum itself: xUnit requires a public
    // test method, and Sites.Domain's enums are internal (visible here only through InternalsVisibleTo),
    // so naming one in a public signature is CS0051. The InlineData arguments still read as the enum --
    // the cast is compile-time, inside a context that does have access.

    [Theory]
    [InlineData((int)SiteStatus.Draft)]
    [InlineData((int)SiteStatus.Approved)]
    [InlineData((int)SiteStatus.Rejected)]
    public void Decide_IsRefusedFromEveryStatusButSubmitted(int status)
    {
        var site = SiteAt((SiteStatus)status);

        var result = site.Decide(approved: true, Guid.NewGuid(), DecidedAt);

        Assert.Equal(SiteErrors.InvalidTransition, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal((SiteStatus)status, site.Status);
    }

    [Theory]
    [InlineData((int)SiteStatus.Submitted)]
    [InlineData((int)SiteStatus.Approved)]
    [InlineData((int)SiteStatus.Rejected)]
    public void Submit_IsRefusedFromEveryStatusButDraft(int status)
    {
        var site = SiteAt((SiteStatus)status);
        var stampBefore = site.SubmittedAtUtc;

        var result = site.Submit(SubmittedAt);

        Assert.Equal(SiteErrors.InvalidTransition, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal((SiteStatus)status, site.Status);
        Assert.Equal(stampBefore, site.SubmittedAtUtc);
    }

    [Theory]
    [InlineData((int)SiteStatus.Draft)]
    [InlineData((int)SiteStatus.Submitted)]
    [InlineData((int)SiteStatus.Approved)]
    public void ReopenForRework_IsRefusedFromEveryStatusButRejected(int status)
    {
        var site = SiteAt((SiteStatus)status);

        var result = site.ReopenForRework();

        Assert.Equal(SiteErrors.InvalidTransition, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal((SiteStatus)status, site.Status);
    }

    /// <summary>
    /// The address guard. <c>DraftSite()</c> in the backend harness always had an address, so this
    /// branch had zero coverage there despite being the reason the rule lives in the domain at all —
    /// the database constraint alone would return a 500 instead of a 400 naming the field.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_WithoutAnAddress_IsRefused(string? address)
    {
        var site = DraftSite(address);

        var result = site.Submit(SubmittedAt);

        Assert.Equal(SiteErrors.AddressRequiredToSubmit, result.Error);
        Assert.Equal(400, result.Error.Status);
        Assert.Equal(SiteStatus.Draft, site.Status);
        Assert.Null(site.SubmittedAtUtc);
    }

    [Fact]
    public void Decide_WithoutADecider_IsRefused()
    {
        var site = SubmittedSite();

        var result = site.Decide(approved: true, Guid.Empty, DecidedAt);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
        Assert.Equal(SiteStatus.Submitted, site.Status);
        Assert.Null(site.DecidedById);
    }

    /// <summary>
    /// The header half of the approved-site lock. The backend tests only that RECORD writes are refused
    /// on an approved site; <c>Site.Update</c>'s own guard was never exercised.
    /// </summary>
    [Fact]
    public void Update_OnAnApprovedSite_IsRefusedAndChangesNothing()
    {
        var site = DecidedSite(approved: true);

        var result = site.Update(new SiteData(Address: "Somewhere else", Notes: "sneaky"));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("12 Nasr Road", site.Address);
        Assert.Null(site.Notes);
    }

    /// <summary>
    /// <b>B12, decided at P5F-02.</b> The backend blocks <c>Approved</c> only, so a survey awaiting a
    /// decision stayed writable and an approval could attest to content that had since changed —
    /// invisibly, because <c>ck_sites_status_stamps</c> records who approved and when but nothing
    /// records what they saw. This asserts the divergence: <c>Submitted</c> is frozen, with the same
    /// specific error and the same unmutated-entity check as the approved case.
    /// </summary>
    [Fact]
    public void Update_OnASubmittedSite_IsRefusedAndChangesNothing()
    {
        var site = SubmittedSite();

        var result = site.Update(new SiteData(Address: "Somewhere else", Notes: "changed after review"));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("12 Nasr Road", site.Address);
        Assert.Null(site.Notes);
        Assert.False(site.IsEditable);
    }

    /// <summary>
    /// The whole editability rule as one statement, so a future change to <see cref="Site.IsEditable"/>
    /// cannot alter one status without this failing. Named for what it asserts rather than for what it
    /// excludes: the earlier name (<c>…AtEveryStatusButApproved</c>) was true of the backend's rule and
    /// would have become a lie the moment the rule changed.
    /// </summary>
    [Theory]
    [InlineData((int)SiteStatus.Draft, true)]
    [InlineData((int)SiteStatus.Rejected, true)]
    [InlineData((int)SiteStatus.Submitted, false)]
    [InlineData((int)SiteStatus.Approved, false)]
    public void Update_IsAllowedInDraftAndRejectedOnly(int status, bool editable)
    {
        var site = SiteAt((SiteStatus)status);

        Assert.Equal(editable, site.IsEditable);

        var result = site.Update(new SiteData(Address: "9 Tahrir St", Notes: "second visit"));

        if (editable)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal("9 Tahrir St", site.Address);
            Assert.Equal("second visit", site.Notes);
        }
        else
        {
            Assert.Equal(SiteErrors.SiteLocked, result.Error);
            Assert.Equal("12 Nasr Road", site.Address);
        }
    }

    /// <summary>
    /// The correction path that makes freezing <c>Submitted</c> affordable rather than a dead end:
    /// reject → reopen → editable again. Without this, B12's fix would trade an invisible divergence for
    /// a survey nobody can fix.
    /// </summary>
    [Fact]
    public void ReopenForRework_RestoresEditabilityAfterASubmittedSiteWasFrozen()
    {
        var site = SubmittedSite();
        Assert.False(site.IsEditable);

        // Rejected is already editable -- the reject itself is the unlock.
        site.Decide(approved: false, Guid.NewGuid(), DecidedAt, "Remeasure the shaft.");
        Assert.True(site.IsEditable);

        Assert.True(site.ReopenForRework().IsSuccess);
        Assert.Equal(SiteStatus.Draft, site.Status);
        Assert.True(site.IsEditable);
        Assert.True(site.Update(new SiteData(Address: "9 Tahrir St")).IsSuccess);
        Assert.Equal("9 Tahrir St", site.Address);
    }

    // ── Create and Apply guards ────────────────────────────────────────────────

    [Fact]
    public void Create_WithoutAProjectOrAuthorOrCode_IsRefused()
    {
        var data = new SiteData();

        Assert.Equal(
            SiteErrors.InvalidData,
            Site.Create(Guid.Empty, "SITE-00001", Guid.NewGuid(), data).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            Site.Create(Guid.NewGuid(), "SITE-00001", Guid.Empty, data).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            Site.Create(Guid.NewGuid(), "   ", Guid.NewGuid(), data).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_RejectsANonPositiveArea(decimal area)
    {
        var result = Site.Create(Guid.NewGuid(), "SITE-1", Guid.NewGuid(), new SiteData(AreaSqm: area));

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(201)]
    public void Apply_RejectsAFloorOutsideTheBuildableRange(int floor)
    {
        var result = Site.Create(Guid.NewGuid(), "SITE-1", Guid.NewGuid(), new SiteData(FloorNo: floor));

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(200)]
    public void Apply_AcceptsTheBoundaryFloors(int floor)
    {
        var result = Site.Create(Guid.NewGuid(), "SITE-1", Guid.NewGuid(), new SiteData(FloorNo: floor));

        Assert.True(result.IsSuccess);
        Assert.Equal(floor, result.Value!.FloorNo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Apply_RejectsAUnitsPerFloorOutsideRange(int units)
    {
        var result = Site.Create(
            Guid.NewGuid(), "SITE-1", Guid.NewGuid(), new SiteData(UnitsPerFloor: units));

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Apply_TrimsTextAndCollapsesBlanksToNull()
    {
        var site = Site.Create(
            Guid.NewGuid(), "  SITE-00007  ", Guid.NewGuid(),
            new SiteData(Address: "  12 Nasr Road  ", Notes: "   ", UtilityMetersNote: "  meter 4 ")).Value!;

        Assert.Equal("SITE-00007", site.Code);
        Assert.Equal("12 Nasr Road", site.Address);
        Assert.Null(site.Notes);
        Assert.Equal("meter 4", site.UtilityMetersNote);
    }

    /// <summary>
    /// A failed <c>Apply</c> must not leave a half-built site behind. <c>Create</c> applies AFTER
    /// constructing, so this asserts the failure is surfaced rather than swallowed into a Draft row with
    /// a bad area.
    /// </summary>
    [Fact]
    public void Create_WithInvalidData_ReturnsFailureRatherThanAPartiallyBuiltSite()
    {
        var result = Site.Create(Guid.NewGuid(), "SITE-1", Guid.NewGuid(), new SiteData(AreaSqm: -3));

        Assert.True(result.IsFailure);
        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Deactivate_DoesNotChangeStatus()
    {
        var site = DecidedSite(approved: true);

        site.Deactivate();

        Assert.False(site.IsActive);
        Assert.Equal(SiteStatus.Approved, site.Status);
    }

    private static Site SiteAt(SiteStatus status) => status switch
    {
        SiteStatus.Draft => DraftSite(),
        SiteStatus.Submitted => SubmittedSite(),
        SiteStatus.Approved => DecidedSite(approved: true),
        SiteStatus.Rejected => DecidedSite(approved: false),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
