using SharedKernel;

namespace Sites.Domain;

/// <summary>All editable site fields in one carrier, applied atomically — the <c>CrmBriefHeadData</c>
/// precedent this port already follows for briefs.</summary>
internal sealed record SiteData(
    string? Address = null,
    UnitType? UnitType = null,
    UnitCondition? UnitCondition = null,
    decimal? AreaSqm = null,
    int? FloorNo = null,
    int? UnitsPerFloor = null,
    BuildingKind? BuildingKind = null,
    UnitPosition? Position = null,
    ElevatorStatus? ElevatorStatus = null,
    bool? IsLicensed = null,
    bool? IsRented = null,
    string? UtilityMetersNote = null,
    DateOnly? SurveyedOn = null,
    string? Notes = null);

/// <summary>
/// One site survey — رفع مقاسات الوحدة — for a project.
///
/// <para>The 12 unit columns mirror <c>CrmDesignBrief</c>'s deliberately, and the duplication is the
/// point: the brief records <i>what the client stated</i>, this records <i>what the surveyor
/// measured</i>. They are allowed to disagree, and the disagreement is what drives a variation. Brief
/// columns are never read for engineering output once an Approved site exists.</para>
///
/// <para>Several surveys per project are permitted — a re-measure after demolition, a villa surveyed
/// per floor — so there is no unique index on <see cref="ProjectId"/>.</para>
///
/// <para><b>Cross-module ids are bare Guids with no navigation properties</b>
/// (<see cref="ProjectId"/> → projects, <see cref="CreatedById"/>/<see cref="DecidedById"/> → hr,
/// <see cref="SourceBriefId"/> → crm). The backend entity was already fully de-navigated, so this is a
/// clean lift rather than a rewrite.</para>
/// </summary>
internal sealed class Site : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// <c>SITE-00001</c>, allocated from the <c>sites.site_code_seq</c> sequence inside the create
    /// transaction. Global and monotonic, with no year segment: one sequence cannot reset per year, and
    /// a code that looked per-year while counting from an earlier one would be read wrong. Rollback
    /// leaves gaps, which is correct — this is an identifier, not a count.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public SiteStatus Status { get; private set; }

    // ── The 12 mirrored unit columns — types identical to crm_design_briefs ──

    public string? Address { get; private set; }

    /// <summary>
    /// Nullable here though NOT NULL on the brief: neither enum has an "unknown" member, and inventing
    /// one to serve this table would change the enum product-wide.
    /// </summary>
    public UnitType? UnitType { get; private set; }

    public UnitCondition? UnitCondition { get; private set; }
    public decimal? AreaSqm { get; private set; }
    public int? FloorNo { get; private set; }
    public int? UnitsPerFloor { get; private set; }
    public BuildingKind? BuildingKind { get; private set; }
    public UnitPosition? Position { get; private set; }
    public ElevatorStatus? ElevatorStatus { get; private set; }

    /// <summary>Mirrored for the printed sheet; the brief stays the system of record — a licence is a
    /// legal fact, not a measurable one.</summary>
    public bool? IsLicensed { get; private set; }

    public bool? IsRented { get; private set; }
    public string? UtilityMetersNote { get; private set; }

    // ── Survey-only ──

    /// <summary>The date the visit happened. A survey with no visit date is not defensible in a
    /// dispute.</summary>
    public DateOnly? SurveyedOn { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// The CRM design brief this survey was seeded from, when it was — <b>provenance, not a
    /// dependency</b>. There is deliberately no domain setter, because a site created through the API is
    /// measured work with no stated-values ancestor to point at.
    ///
    /// <para><b>Nothing in this system writes it.</b> The backend's only writer is
    /// <c>BackfillSitesFromBriefs</c>, a data-migration utility that is not ported, so the column stays
    /// null until some future phase makes provenance real. That is why <c>GET /variance</c> is not
    /// ported either (S1-01, [D8]) — it could only ever answer "no source brief" — and why the S1-09
    /// characterization spine deliberately does not claim to prove brief→site provenance.</para>
    ///
    /// <para>This is also the RIGHT direction for this relationship, and the reason
    /// <c>CrmDesignBrief.SiteId</c> was removed at S1-00: a brief does not point at a site.</para>
    /// </summary>
    public Guid? SourceBriefId { get; private set; }

    // ── Lifecycle trail ──

    public Guid CreatedById { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public Guid? DecidedById { get; private set; }
    public string? DecisionNote { get; private set; }

    private Site() { }

    /// <summary>
    /// Whether records and header fields may still be written. The backend expresses this as a
    /// <c>Status == Approved</c> test repeated in the domain service's <c>EnsureSiteEditableAsync</c>
    /// and inline in <c>AddRecordAsync</c>; it is a pure predicate about one row, so it belongs on the
    /// entity. The domain service is not ported — its orchestration lives in handlers, its pure parts
    /// here.
    ///
    /// <para><b>Submitted is frozen too, and that is a deliberate divergence from the backend
    /// (defect B12, decided at P5F-02).</b> The backend blocks <c>Approved</c> only, so a survey
    /// awaiting a decision stayed writable and an approval could attest to content that had since
    /// changed. That is a correctness defect rather than a workflow preference: the approval IS the
    /// engineering record, <c>ck_sites_status_stamps</c> stamps who approved and when, and no audit
    /// trail exists to detect the divergence — so the row would assert an approval of content that no
    /// longer exists, invisibly and unrecoverably.</para>
    ///
    /// <para>The correction path is <see cref="ReopenForRework"/>, which already exists: reject, reopen
    /// to <c>Draft</c>, edit, resubmit. Freezing costs a visible, recoverable reject-reopen cycle. Not
    /// freezing cost an invisible, unrecoverable one.</para>
    ///
    /// <para><c>Rejected</c> stays editable — rework is the whole point of the state.</para>
    /// </summary>
    public bool IsEditable => Status is SiteStatus.Draft or SiteStatus.Rejected;

    public static Result<Site> Create(Guid projectId, string code, Guid createdById, SiteData data)
    {
        if (projectId == Guid.Empty || createdById == Guid.Empty || string.IsNullOrWhiteSpace(code))
        {
            return SiteErrors.InvalidData;
        }

        var site = new Site
        {
            ProjectId = projectId,
            Code = code.Trim(),
            CreatedById = createdById,
            Status = SiteStatus.Draft,
        };

        Result applied = site.Apply(data);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        return site;
    }

    /// <summary>
    /// Edit in place. Refused once Submitted — an approved survey is the engineering record, and the way
    /// to change it is a new survey, not a quiet edit under the drawings already derived from it; a
    /// submitted one is frozen a step earlier so the decision and the content cannot diverge (B12).
    /// </summary>
    public Result Update(SiteData data)
    {
        if (!IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        return Apply(data);
    }

    /// <summary>
    /// Draft → Submitted. The address requirement is enforced here rather than only by the database
    /// check constraint, so the caller gets a 400 naming the field instead of a 500.
    /// </summary>
    public Result Submit(DateTime submittedAtUtc)
    {
        if (Status != SiteStatus.Draft)
        {
            return SiteErrors.InvalidTransition;
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            return SiteErrors.AddressRequiredToSubmit;
        }

        Status = SiteStatus.Submitted;
        SubmittedAtUtc = submittedAtUtc;
        return Result.Success();
    }

    /// <summary>Submitted → Approved or Rejected. Who and when are stamped together, matching the
    /// row's own status/stamp check constraint.</summary>
    public Result Decide(bool approved, Guid decidedById, DateTime decidedAtUtc, string? note = null)
    {
        if (Status != SiteStatus.Submitted)
        {
            return SiteErrors.InvalidTransition;
        }

        if (decidedById == Guid.Empty)
        {
            return SiteErrors.InvalidData;
        }

        Status = approved ? SiteStatus.Approved : SiteStatus.Rejected;
        DecidedById = decidedById;
        DecidedAtUtc = decidedAtUtc;
        DecisionNote = Normalize(note);
        return Result.Success();
    }

    /// <summary>
    /// Rejected → Draft, clearing all four stamps so the row satisfies its status/stamp check
    /// constraint again. The only way back: a rejected survey is reworked, not re-decided.
    /// </summary>
    public Result ReopenForRework()
    {
        if (Status != SiteStatus.Rejected)
        {
            return SiteErrors.InvalidTransition;
        }

        Status = SiteStatus.Draft;
        SubmittedAtUtc = null;
        DecidedAtUtc = null;
        DecidedById = null;
        DecisionNote = null;
        return Result.Success();
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private Result Apply(SiteData d)
    {
        if (d.AreaSqm is <= 0)
        {
            return SiteErrors.InvalidData;
        }

        if (d.FloorNo is < -5 or > 200)
        {
            return SiteErrors.InvalidData;
        }

        if (d.UnitsPerFloor is < 1 or > 100)
        {
            return SiteErrors.InvalidData;
        }

        Address = Normalize(d.Address);
        UnitType = d.UnitType;
        UnitCondition = d.UnitCondition;
        AreaSqm = d.AreaSqm;
        FloorNo = d.FloorNo;
        UnitsPerFloor = d.UnitsPerFloor;
        BuildingKind = d.BuildingKind;
        Position = d.Position;
        ElevatorStatus = d.ElevatorStatus;
        IsLicensed = d.IsLicensed;
        IsRented = d.IsRented;
        UtilityMetersNote = Normalize(d.UtilityMetersNote);
        SurveyedOn = d.SurveyedOn;
        Notes = Normalize(d.Notes);
        return Result.Success();
    }

    private static string? Normalize(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
