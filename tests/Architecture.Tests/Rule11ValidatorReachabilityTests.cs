using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Messaging;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 11: every FluentValidation validator in a <c>*.Application</c> assembly is reachable, meaning it
/// validates a command or query whose handler runs through <c>ValidationDecorator</c>.
///
/// <para>The failure this guards against already happened once, silently and for a long time. The
/// <c>AddModuleCqrs</c> scan registers every <c>IValidator&lt;T&gt;</c> it finds, whether or not anything
/// ever resolves it. Until HR/Identity Phase 0 (Aug 0.1), queries had no validating decorator, so all 33
/// query validators (Finance 27, HR 2, Approvals 2, CRM 2) were registered and never invoked. The code
/// read as if paging was range-checked, and it wasn't. A validator on a DTO, or on a message nobody
/// handles, fails the same way.</para>
///
/// <para>What "reachable" means here, mechanically:</para>
/// <list type="number">
/// <item><c>ValidationDecorator</c> has a decorator for each of the three handler shapes
/// <c>AddModuleCqrs</c> decorates (<c>ICommandHandler&lt;&gt;</c>, <c>ICommandHandler&lt;,&gt;</c> and
/// <c>IQueryHandler&lt;,&gt;</c>). That is what puts validation in every pipeline;
/// <c>ValidationDecoratorPipelineTests</c> proves the decorators run.</item>
/// <item>The validator's <c>T</c> implements <see cref="IBaseCommand"/> or <see cref="IBaseQuery"/>.</item>
/// <item>A handler for <c>T</c> lives in the <b>same</b> assembly as the validator. <c>AddModuleCqrs</c>
/// registers handlers and validators from the one assembly it is given, so a handler in the same
/// assembly is a handler <c>AddModuleCqrs</c> decorated.</item>
/// </list>
///
/// <para>The one exemption is a child validator: a validator for a nested DTO, run only through another
/// validator's <c>SetValidator</c>. It is reachable through its parent, not through DI. Each exemption
/// is checked rather than trusted: it must still exist, and some other validator in its assembly must
/// still depend on it.</para>
/// </summary>
public class Rule11ValidatorReachabilityTests
{
    private const string AbstractValidatorDefinition = "FluentValidation.AbstractValidator`1";

    /// <summary>
    /// Validators reached through a parent validator's <c>SetValidator</c>, never resolved from DI.
    /// </summary>
    private static readonly string[] ChildValidators =
    [
        // CrmBriefHeadData is the head payload nested inside CreateCrmDesignBriefCommand and
        // UpdateCrmBriefHeadCommand; both command validators run it via SetValidator(new ...()).
        "Crm.Application.Features.Briefs.CrmBriefHeadDataValidator",

        // CrmLeadIntakeData is the intake payload nested inside SaveCrmLeadIntakeCommand, run via
        // SetValidator(new ...()); its Residential block is nested again, run the same way from it.
        "Crm.Application.Features.Leads.CrmLeadIntakeDataValidator",
        "Crm.Application.Features.Leads.CrmLeadIntakeResidentialDataValidator",
    ];

    private static readonly Type[] DecoratedHandlerShapes =
        [typeof(ICommandHandler<>), typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    [Fact]
    public void ValidationDecorator_CoversEveryHandlerShapeTheCqrsPipelineDecorates()
    {
        Type? validationDecorator = typeof(ModuleCqrsRegistration).Assembly
            .GetType("BuildingBlocks.Behaviors.ValidationDecorator");
        Assert.NotNull(validationDecorator);

        Type[] implementedShapes = [.. validationDecorator!
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .Distinct()];

        foreach (Type shape in DecoratedHandlerShapes)
        {
            Assert.True(implementedShapes.Contains(shape),
                $"ValidationDecorator has no nested decorator implementing {shape.Name}. Every validator "
                + "for that handler shape is registered and never runs.");
        }
    }

    [Fact]
    public void EveryApplicationValidator_TargetsAHandledCommandOrQuery()
    {
        IReadOnlyList<Assembly> applicationAssemblies = ModuleAssemblies.WithSuffix(".Application");
        List<string> violations = [];

        // PC-03 liveness. The loop below passes trivially if discovery finds no validators: an assembly
        // that fails to load, or a FluentValidation rename that breaks the base-type match. Counting the
        // validators actually checked is what makes the rule falsifiable.
        int validatorsChecked = 0;
        int childValidatorsChecked = 0;

        foreach (Assembly assembly in applicationAssemblies)
        {
            Type[] types = [.. ModuleAssemblies.SafeGetTypes(assembly)];
            HashSet<Type> handledMessages = [.. types
                .Where(t => t is { IsAbstract: false, IsInterface: false })
                .SelectMany(t => t.GetInterfaces())
                .Where(i => i.IsGenericType && DecoratedHandlerShapes.Contains(i.GetGenericTypeDefinition()))
                .Select(i => i.GetGenericArguments()[0])];

            foreach (Type validator in types.Where(t => t is { IsClass: true, IsAbstract: false }))
            {
                Type? target = ValidatedType(validator);
                if (target is null)
                {
                    continue;
                }

                if (ChildValidators.Contains(validator.FullName))
                {
                    childValidatorsChecked++;
                    if (!IsUsedByAnotherValidator(assembly, validator))
                    {
                        violations.Add($"{validator.FullName} is listed as a child validator, but no other "
                            + "validator in its assembly uses it any more. Remove it from ChildValidators.");
                    }

                    continue;
                }

                validatorsChecked++;

                bool isMessage = typeof(IBaseCommand).IsAssignableFrom(target)
                    || typeof(IBaseQuery).IsAssignableFrom(target);
                if (!isMessage)
                {
                    violations.Add($"{validator.FullName} validates {target.FullName}, which is neither a "
                        + "command nor a query. Nothing resolves IValidator<T> for it, so it never runs.");
                }
                else if (!handledMessages.Contains(target))
                {
                    violations.Add($"{validator.FullName} validates {target.FullName}, but no handler for "
                        + $"it lives in {assembly.GetName().Name}, so AddModuleCqrs never decorates one.");
                }
            }
        }

        foreach (string listed in ChildValidators)
        {
            bool exists = applicationAssemblies.Any(a => a.GetType(listed) is not null);
            if (!exists)
            {
                violations.Add($"ChildValidators lists {listed}, which no longer exists. Remove the entry.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // Counted at HR/Identity Phase 0: 180 command/query validators across the nine *.Application
        // assemblies (Notifications' has none), plus one child validator. Adding validators keeps this true; a drop
        // means a module's validators stopped being discovered.
        Assert.True(validatorsChecked >= 180,
            $"Checked only {validatorsChecked} validators across {applicationAssemblies.Count} *.Application "
            + "assemblies, expected at least 180. Discovery is broken, so this rule is checking nothing.");
        Assert.True(childValidatorsChecked == ChildValidators.Length,
            $"Found {childValidatorsChecked} of {ChildValidators.Length} listed child validators.");
    }

    private static Type? ValidatedType(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition().FullName == AbstractValidatorDefinition)
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool IsUsedByAnotherValidator(Assembly assembly, Type childValidator) =>
        Types.InAssembly(assembly)
            .That()
            .HaveDependencyOn(childValidator.FullName!)
            .GetTypes()
            .Select(OutermostType)
            .Any(t => t != childValidator && ValidatedType(t) is not null);

    // A dependency taken inside a lambda is recorded on the compiler-generated closure type, which is
    // nested in the validator that wrote it.
    private static Type OutermostType(Type type)
    {
        Type current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}
