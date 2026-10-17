using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Value-like child record persisted as JSON inside the owning <see cref="Client"/> row (G9-07's
/// <c>ClientConfiguration</c> owns it via <c>OwnsMany(...).ToJson(...)</c>) — ported verbatim from the
/// backend's own comment: "not an Entity / not IAuditable — has no DB identity of its own. The Id is
/// kept only as a stable reference for API updates/deletes."
/// </summary>
internal sealed class ClientChild
{
    public Guid Id { get; private set; }
    public char Gender { get; private set; }
    public short Age { get; private set; }

    private ClientChild() { }

    private ClientChild(Guid id, char gender, short age)
    {
        Id = id;
        Gender = gender;
        Age = age;
    }

    public static Result<ClientChild> Create(char gender, short age)
    {
        Result validation = Validate(gender, age);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new ClientChild(Guid.CreateVersion7(), gender, age);
    }

    public Result Update(char gender, short age)
    {
        Result validation = Validate(gender, age);
        if (validation.IsFailure)
        {
            return validation;
        }

        Gender = gender;
        Age = age;
        return Result.Success();
    }

    private static Result Validate(char gender, short age)
    {
        if (gender is not ('M' or 'F'))
        {
            return ClientChildErrors.InvalidGender;
        }

        if (age is < 0 or > 120)
        {
            return ClientChildErrors.InvalidAge;
        }

        return Result.Success();
    }
}
