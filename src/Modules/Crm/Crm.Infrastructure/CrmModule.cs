using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Crm.Contracts;
using Crm.Infrastructure.Decorators;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;

namespace Crm.Infrastructure;

/// <summary>
/// `Register` still grows at G9-14 (endpoints/permissions are already wired; only the module-registration
/// side effects specific to the anonymous surface remain) — same incremental shape `DocumentsModule`'s
/// own history took across G7-01→G7-08.
/// </summary>
internal sealed class CrmModule : IModule
{
    public string Name => "Crm";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<CrmDbContext>(cfg);

        services.AddScoped<ICrmAccessGuard, CrmAccessGuard>();
        services.AddScoped<ICrmGateReader, CrmGateReader>();
        services.AddScoped<CrmApprovalOutcomes>();
        services.AddScoped<CrmProjectScope>();
        services.AddScoped<IClientDirectory, ClientDirectory>();
        services.AddScoped<ICrmSchedulerService, CrmSchedulerService>();

        // Added at S1-01 for Sites: SiteRecord.SpaceId's "same project as the site" invariant cannot
        // be expressed without a CRM read, and reading crm.crm_spaces directly is forbidden.
        services.AddScoped<ICrmSpaceReader, CrmSpaceReader>();

        // M1-01: this module's first bound options. TimezoneId cannot be checked by a data annotation -
        // a malformed IANA/Windows id only fails when FindSystemTimeZoneById is actually called - so
        // .Validate(...) exercises it at startup, turning a per-request TimeZoneNotFoundException into
        // a boot failure. Same shape as IdentityModule's JwtOptions registration.
        services
            .AddOptions<OfficeHoursOptions>()
            .Bind(cfg.GetSection(OfficeHoursOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o =>
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(o.TimezoneId);
                    return true;
                }
                catch (TimeZoneNotFoundException)
                {
                    return false;
                }
                catch (InvalidTimeZoneException)
                {
                    return false;
                }
            }, "OfficeHours:TimezoneId is not a recognized time zone id.")
            .ValidateOnStart();

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, CrmPermissions.All));

        var applicationAssembly = Assembly.Load("Crm.Application");
        services.AddModuleCqrs<CrmDbContext>(applicationAssembly);

        // Must run AFTER AddModuleCqrs - Scrutor's Decorate wraps in call order, so the scope check
        // needs the Validation→Logging→DomainEventDispatch chain already registered to wrap outermost.
        services.AddCrmScopeDecorator(typeof(ICrmScoped).Assembly);
        services.AddCrmProjectScopeDecorator(typeof(ICrmProjectScoped).Assembly);

        // The three IGateConditionTypeHandler implementations (Crm.Application/Features/Gates) are
        // `internal` there, with no InternalsVisibleTo grant to Crm.Infrastructure - Scrutor's scan
        // needs only the PUBLIC interface type, same technique DocumentsModule/ProjectsModule already
        // use for their own gate-handler registrations.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IGateConditionTypeHandler>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // N1-05: the meeting-reminder and period-close jobs, run by BuildingBlocks' single
        // PeriodicJobWorker. Same Scrutor technique as the gate handlers directly above, for the same
        // reason - the jobs are `internal` in Crm.Application.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IPeriodicJob>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // ApprovalDecidedIntegrationEventHandler lives in THIS assembly (Infrastructure), not
        // Application - it calls out to Approvals.Contracts.IIntegrationEventHandler<...> and needs
        // ICrmGateReader, same "Infrastructure owns handlers that call another module's contract"
        // reasoning DocumentsModule's own registration documents. CRM is the third subscriber to
        // ApprovalDecidedIntegrationEvent (after Projects on GateCondition, Documents on
        // ProjectDocument) - safe because AddModuleIntegrationEvents (G7-07's fix) only decorates
        // descriptors added by THIS call's own scan, never re-wraps another module's registration.
        services.AddModuleIntegrationEvents<CrmDbContext>(typeof(CrmModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(CrmModule).Assembly);
}
