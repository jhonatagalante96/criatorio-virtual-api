using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Dashboard;
using CriatorioVirtual.Application.BreedingFarmStatistics;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Application.Species;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application;
using CriatorioVirtual.Infrastructure.Messaging;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.BreedingFarms;
using CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;
using CriatorioVirtual.Infrastructure.Birds;
using CriatorioVirtual.Infrastructure.Competitions;
using CriatorioVirtual.Infrastructure.Dashboard;
using CriatorioVirtual.Infrastructure.BreedingFarmStatistics;
using CriatorioVirtual.Infrastructure.Reproductions;
using CriatorioVirtual.Infrastructure.Species;
using CriatorioVirtual.Infrastructure.Transfers;
using CriatorioVirtual.Infrastructure.Documents;
using CriatorioVirtual.Infrastructure.Reports;
using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace CriatorioVirtual.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructurePersistence(
        this IServiceCollection services,
        string connectionString,
        X509Certificate2 dataProtectionCertificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(dataProtectionCertificate);

        if (!dataProtectionCertificate.HasPrivateKey)
        {
            throw new ArgumentException("The Data Protection certificate must contain a private key.", nameof(dataProtectionCertificate));
        }

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddDbContext<CriatorioVirtualDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema)));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CriatorioVirtualDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddDataProtection()
            .PersistKeysToDbContext<CriatorioVirtualDbContext>()
            .ProtectKeysWithCertificate(dataProtectionCertificate)
            .SetApplicationName("CriatorioVirtual");

        services.AddScoped<ICommandExecutor, CommandExecutor>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<
            ICommandHandler<CreateBillingSubscriptionCommand, CreateBillingSubscriptionResult>,
            CreateBillingSubscriptionCommandHandler>();
        services.AddScoped<
            ICommandPostProcessor<CreateBillingSubscriptionCommand, CreateBillingSubscriptionResult>,
            CreateBillingSubscriptionPostProcessor>();
        services.AddScoped<
            ICommandHandler<CancelBillingSubscriptionCommand, CancelBillingSubscriptionResult>,
            CancelBillingSubscriptionCommandHandler>();
        services.AddScoped<
            ICommandHandler<RegularizeBillingPaymentCommand, RegularizeBillingPaymentResult>,
            RegularizeBillingPaymentCommandHandler>();
        services.AddScoped<
            ICommandPostProcessor<RegularizeBillingPaymentCommand, RegularizeBillingPaymentResult>,
            RegularizeBillingPaymentPostProcessor>();
        services.AddScoped<ReceiveAsaasWebhookCommandHandler>();
        services.AddScoped<ICommandHandler<ReceiveAsaasWebhookCommand, ReceiveAsaasWebhookResult>>(
            serviceProvider => serviceProvider.GetRequiredService<ReceiveAsaasWebhookCommandHandler>());
        services.AddScoped<ICommandHandler<ProcessAsaasWebhookEventCommand, ProcessAsaasWebhookEventResult>>(
            serviceProvider => serviceProvider.GetRequiredService<ReceiveAsaasWebhookCommandHandler>());
        services.AddScoped<IAsaasWebhookEventProcessingService, AsaasWebhookEventProcessingService>();
        services.AddHostedService<AsaasWebhookEventRetryWorker>();
        services.AddScoped<ISubscriptionGracePeriodBlockingService, SubscriptionGracePeriodBlockingService>();
        services.AddHostedService<SubscriptionGracePeriodBlockingWorker>();
        services.AddScoped<
            ICommandPostProcessor<CancelBillingSubscriptionCommand, CancelBillingSubscriptionResult>,
            CancelBillingSubscriptionPostProcessor>();
        services
            .AddOptions<DocumentRenderingOptions>()
            .BindConfiguration(DocumentRenderingOptions.SectionName)
            .Validate(
                options => options.MaxConcurrentRenders is > 0 and <= DocumentRenderingOptions.MaximumConcurrentRenders,
                $"DocumentRendering:MaxConcurrentRenders must be between 1 and {DocumentRenderingOptions.MaximumConcurrentRenders}.")
            .Validate(
                options => options.RenderTimeoutSeconds > 0,
                "DocumentRendering:RenderTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();
        services.AddSingleton<ChromiumHtmlToPdfRenderer>(serviceProvider =>
            new ChromiumHtmlToPdfRenderer(
                serviceProvider.GetRequiredService<IOptions<DocumentRenderingOptions>>().Value));
        services.AddSingleton<IHtmlToPdfRenderer>(serviceProvider =>
            serviceProvider.GetRequiredService<ChromiumHtmlToPdfRenderer>());
        services.AddSingleton<IHtmlToPngRenderer>(serviceProvider =>
            serviceProvider.GetRequiredService<ChromiumHtmlToPdfRenderer>());
        services.AddSingleton<IDocumentRenderer, PdfDocumentRenderer>();
        services.AddScoped<BirdDocumentGenerationSession>();
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<BirdDocumentGenerationSession>());
        services.AddScoped<ICommandPreProcessor<GenerateBirdDocumentCommand>, GenerateBirdDocumentPreProcessor>();
        services.AddScoped<ICommandHandler<GenerateBirdDocumentCommand, GenerateBirdDocumentResult>, GenerateBirdDocumentCommandHandler>();
        services
            .AddOptions<BadgeBatchOptions>()
            .BindConfiguration(BadgeBatchOptions.SectionName)
            .ValidateOnStart()
            .Services
            .AddSingleton<IValidateOptions<BadgeBatchOptions>, BadgeBatchOptionsValidator>();
        services.AddSingleton<IPdfDocumentAssembler, PdfDocumentAssembler>();
        services.AddScoped<BadgeBatchGenerationSession>();
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<BadgeBatchGenerationSession>());
        services.AddScoped<ICommandPreProcessor<GenerateBadgeBatchCommand>, GenerateBadgeBatchPreProcessor>();
        services.AddScoped<ICommandHandler<GenerateBadgeBatchCommand, GenerateBadgeBatchResult>, GenerateBadgeBatchCommandHandler>();
        services.AddScoped<ReissueBirdDocumentSession>();
        services.AddScoped<ICommandPreProcessor<ReissueBirdDocumentCommand>, ReissueBirdDocumentPreProcessor>();
        services.AddScoped<ICommandHandler<ReissueBirdDocumentCommand, ReissueBirdDocumentResult>, ReissueBirdDocumentCommandHandler>();
        services.AddScoped<IQueryHandler<ListBirdDocumentsQuery, ListBirdDocumentsResult>, ListBirdDocumentsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdDocumentContentQuery, GetBirdDocumentContentResult>, GetBirdDocumentContentQueryHandler>();
        services.AddSingleton<IBirdsReportRenderer, BirdsReportPdfRenderer>();
        services.AddScoped<IQueryHandler<GenerateBirdsReportQuery, GenerateBirdsReportResult>, GenerateBirdsReportQueryHandler>();
        services.AddScoped<AccountRegistrationService>();
        services.AddScoped<IAccountEmailConfirmationService, AccountEmailConfirmationService>();
        services.AddScoped<IAccountPasswordService, AccountPasswordService>();
        services.AddScoped<IAccountPasskeyService, AccountPasskeyService>();
        services.AddScoped<IAccountPasskeyAuthenticationService, AccountPasskeyAuthenticationService>();
        services.AddScoped<IAccountSessionService, AccountSessionService>();
        services.AddScoped<IUserAvatarStorage, PrivateObjectStorageUserAvatarAdapter>();
        services.AddScoped<UserAvatarUploadSession>();
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<UserAvatarUploadSession>());
        services.AddScoped<ICommandPreProcessor<UploadUserAvatarCommand>, UploadUserAvatarPreProcessor>();
        services.AddScoped<
            ICommandHandler<UploadUserAvatarCommand, UploadUserAvatarResult>,
            UploadUserAvatarCommandHandler>();
        services.AddScoped<
            ICommandPostProcessor<UploadUserAvatarCommand, UploadUserAvatarResult>,
            UserAvatarStoragePostProcessor>();
        services.AddScoped<
            ICommandHandler<RemoveUserAvatarCommand, RemoveUserAvatarResult>,
            RemoveUserAvatarCommandHandler>();
        services.AddScoped<
            ICommandPostProcessor<RemoveUserAvatarCommand, RemoveUserAvatarResult>,
            UserAvatarStoragePostProcessor>();
        services.AddScoped<
            IQueryHandler<GetUserAvatarContentQuery, GetUserAvatarContentResult>,
            GetUserAvatarContentQueryHandler>();
        services.AddScoped<IGoogleAccountAuthenticationService, GoogleAccountAuthenticationService>();
        services.AddScoped<ICommandHandler<CreateBreedingFarmCommand, CreateBreedingFarmResult>, CreateBreedingFarmCommandHandler>();
        services.AddScoped<ICommandHandler<SelectBreedingFarmCommand, SelectBreedingFarmResult>, SelectBreedingFarmCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBreedingFarmSettingsCommand, UpdateBreedingFarmSettingsResult>, UpdateBreedingFarmSettingsCommandHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmGalleryQuery, GetBreedingFarmGalleryResult>, GetBreedingFarmGalleryQueryHandler>();
        services.AddScoped<ICommandHandler<UpdateBreedingFarmGalleryCaptionCommand, UpdateBreedingFarmGalleryCaptionResult>, UpdateBreedingFarmGalleryCaptionCommandHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmGalleryMediaContentQuery, GetBreedingFarmGalleryMediaContentResult>, GetBreedingFarmGalleryMediaContentQueryHandler>();
        services.AddScoped<BreedingFarmVisualIdentityUploadSession>();
        services.AddSingleton<IVisualIdentityTemplateCatalog, BreedingFarmVisualIdentityTemplateCatalog>();
        services.AddSingleton<IVisualIdentityTemplateImageRenderer, BreedingFarmVisualIdentityTemplateImageRenderer>();
        services.AddScoped<ApplyBreedingFarmVisualIdentityTemplateSession>();
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<BreedingFarmVisualIdentityUploadSession>());
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplyBreedingFarmVisualIdentityTemplateSession>());
        services.AddScoped<ICommandPreProcessor<UploadBreedingFarmVisualIdentityCommand>, UploadBreedingFarmVisualIdentityPreProcessor>();
        services.AddScoped<ICommandHandler<UploadBreedingFarmVisualIdentityCommand, UploadBreedingFarmVisualIdentityResult>, UploadBreedingFarmVisualIdentityCommandHandler>();
        services.AddScoped<ICommandPostProcessor<UploadBreedingFarmVisualIdentityCommand, UploadBreedingFarmVisualIdentityResult>, BreedingFarmVisualIdentityStoragePostProcessor>();
        services.AddScoped<ICommandHandler<RemoveBreedingFarmVisualIdentityCommand, RemoveBreedingFarmVisualIdentityResult>, RemoveBreedingFarmVisualIdentityCommandHandler>();
        services.AddScoped<ICommandPostProcessor<RemoveBreedingFarmVisualIdentityCommand, RemoveBreedingFarmVisualIdentityResult>, BreedingFarmVisualIdentityStoragePostProcessor>();
        services.AddScoped<IQueryHandler<GetBreedingFarmVisualIdentityQuery, GetBreedingFarmVisualIdentityResult>, GetBreedingFarmVisualIdentityQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmVisualIdentityContentQuery, GetBreedingFarmVisualIdentityContentResult>, GetBreedingFarmVisualIdentityContentQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmVisualIdentityTemplatesQuery, IReadOnlyList<BreedingFarmVisualIdentityTemplateCatalogItem>>, GetBreedingFarmVisualIdentityTemplatesQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmVisualIdentityTemplatePreviewQuery, GetBreedingFarmVisualIdentityTemplatePreviewResult>, GetBreedingFarmVisualIdentityTemplatePreviewQueryHandler>();
        services.AddScoped<IQueryHandler<PreviewBreedingFarmVisualIdentityTemplateQuery, PreviewBreedingFarmVisualIdentityTemplateResult>, PreviewBreedingFarmVisualIdentityTemplateQueryHandler>();
        services.AddScoped<ICommandPreProcessor<ApplyBreedingFarmVisualIdentityTemplateCommand>, ApplyBreedingFarmVisualIdentityTemplatePreProcessor>();
        services.AddScoped<ICommandHandler<ApplyBreedingFarmVisualIdentityTemplateCommand, ApplyBreedingFarmVisualIdentityTemplateResult>, ApplyBreedingFarmVisualIdentityTemplateCommandHandler>();
        services.AddScoped<ICommandPostProcessor<ApplyBreedingFarmVisualIdentityTemplateCommand, ApplyBreedingFarmVisualIdentityTemplateResult>, ApplyBreedingFarmVisualIdentityTemplatePostProcessor>();
        services.AddScoped<ICommandHandler<CreateBirdCommand, CreateBirdResult>, CreateBirdCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBirdCommand, UpdateBirdResult>, UpdateBirdCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBirdGenealogyCommand, UpdateBirdGenealogyResult>, UpdateBirdGenealogyCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateExternalGenealogyParentCommand, UpdateExternalGenealogyParentResult>, UpdateExternalGenealogyParentCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteExternalGenealogyParentCommand, DeleteExternalGenealogyParentResult>, DeleteExternalGenealogyParentCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeBirdStatusCommand, ChangeBirdStatusResult>, ChangeBirdStatusCommandHandler>();
        services.AddScoped<ICommandHandler<ReactivateBirdCommand, ReactivateBirdResult>, ReactivateBirdCommandHandler>();
        services.AddScoped<ICommandHandler<SetBirdPrimaryPhotoCommand, SetBirdPrimaryPhotoResult>, SetBirdPrimaryPhotoCommandHandler>();
        services.AddScoped<BirdAttachmentUploadSession>();
        services.AddScoped<ICommandFailureCompensator>(serviceProvider =>
            serviceProvider.GetRequiredService<BirdAttachmentUploadSession>());
        services.AddScoped<ICommandPreProcessor<UploadBirdAttachmentCommand>, UploadBirdAttachmentPreProcessor>();
        services.AddScoped<ICommandHandler<UploadBirdAttachmentCommand, UploadBirdAttachmentResult>, UploadBirdAttachmentCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteBirdAttachmentCommand, DeleteBirdAttachmentResult>, DeleteBirdAttachmentCommandHandler>();
        services.AddScoped<ICommandPostProcessor<DeleteBirdAttachmentCommand, DeleteBirdAttachmentResult>, DeleteBirdAttachmentStorageCleanupProcessor>();
        services.AddScoped<IQueryHandler<ListBirdAttachmentsQuery, ListBirdAttachmentsResult>, ListBirdAttachmentsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdAttachmentContentQuery, GetBirdAttachmentContentResult>, GetBirdAttachmentContentQueryHandler>();
        services.AddScoped<ICommandHandler<CreateReproductionCommand, CreateReproductionResult>, CreateReproductionCommandHandler>();
        services.AddScoped<ICommandHandler<CreateBirdCompetitionCommand, CreateBirdCompetitionResult>, CreateBirdCompetitionCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBirdCompetitionCommand, UpdateBirdCompetitionResult>, UpdateBirdCompetitionCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteBirdCompetitionCommand, DeleteBirdCompetitionResult>, DeleteBirdCompetitionCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateReproductionCommand, UpdateReproductionResult>, UpdateReproductionCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeReproductionStatusCommand, ChangeReproductionStatusResult>, ChangeReproductionStatusCommandHandler>();
        services.AddScoped<ICommandHandler<LinkReproductionOriginCommand, LinkReproductionOriginResult>, LinkReproductionOriginCommandHandler>();
        services.AddScoped<IQueryHandler<ListBirdCompetitionsQuery, ListBirdCompetitionsResult>, ListBirdCompetitionsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdCompetitionQuery, GetBirdCompetitionResult>, GetBirdCompetitionQueryHandler>();
        services.AddScoped<IQueryHandler<ListReproductionsQuery, ListReproductionsResult>, ListReproductionsQueryHandler>();
        services.AddScoped<IQueryHandler<GetReproductionQuery, GetReproductionResult>, GetReproductionQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdQuery, GetBirdResult>, GetBirdQueryHandler>();
        services.AddScoped<IQueryHandler<GetDashboardQuery, GetDashboardResult>, GetDashboardQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmStatisticsQuery, GetBreedingFarmStatisticsResult>, GetBreedingFarmStatisticsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdGenealogyQuery, GetBirdGenealogyResult>, GetBirdGenealogyQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdEligibilityQuery, GetBirdEligibilityResult>, GetBirdEligibilityQueryHandler>();
        services.AddScoped<IQueryHandler<ListBirdsQuery, ListBirdsResult>, ListBirdsQueryHandler>();
        services.AddScoped<IQueryHandler<SearchBirdParentOptionsQuery, SearchBirdParentOptionsResult>, SearchBirdParentOptionsQueryHandler>();
        services.AddScoped<ICommandHandler<RequestInternalTransferCommand, RequestInternalTransferResult>, RequestInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<AcceptInternalTransferCommand, AcceptInternalTransferResult>, AcceptInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<RejectInternalTransferCommand, RejectInternalTransferResult>, RejectInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<CancelInternalTransferCommand, CancelInternalTransferResult>, CancelInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<CompleteExternalTransferCommand, CompleteExternalTransferResult>, CompleteExternalTransferCommandHandler>();
        services.AddScoped<IQueryHandler<ListInternalTransferRequestsQuery, ListInternalTransferRequestsResult>, ListInternalTransferRequestsQueryHandler>();
        services.AddScoped<IQueryHandler<GetInternalTransferRequestQuery, GetInternalTransferRequestResult>, GetInternalTransferRequestQueryHandler>();
        services.AddScoped<IQueryHandler<SearchInternalTransferDestinationsQuery, SearchInternalTransferDestinationsResult>, SearchInternalTransferDestinationsQueryHandler>();
        services.AddScoped<IQueryHandler<ListBreedingFarmsQuery, BreedingFarmSelectionResult>, ListBreedingFarmsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmSettingsQuery, BreedingFarmSettingsResult?>, GetBreedingFarmSettingsQueryHandler>();
        services.AddScoped<IQueryHandler<SearchSpeciesQuery, IReadOnlyCollection<SpeciesSearchResult>>, SearchSpeciesQueryHandler>();
        services.AddScoped<IQueryHandler<GetSubscriptionQuery, GetSubscriptionResult>, GetSubscriptionQueryHandler>();
        services.AddScoped<IQueryHandler<ListPaymentsQuery, ListPaymentsResult>, ListPaymentsQueryHandler>();
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddSingleton<IAuthenticationEmailConfirmationThrottle, AuthenticationEmailConfirmationThrottle>();
        services.AddMessagingHandlers(typeof(ApplicationAssemblyMarker).Assembly);

        return services;
    }
}

internal static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var implementation in assembly.DefinedTypes.Where(type => type is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var contract in implementation.ImplementedInterfaces.Where(IsMessagingContract))
            {
                services.AddScoped(contract, implementation);
            }
        }

        return services;
    }

    private static bool IsMessagingContract(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ICommandHandler<,>) || definition == typeof(ICommandPreProcessor<>) || definition == typeof(IQueryHandler<,>));
}
