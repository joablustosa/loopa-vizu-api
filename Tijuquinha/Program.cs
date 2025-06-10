using Quartz;
using Tijuquinha;
using Tijuquinha.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuração do Quartz
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var jobKey = new JobKey("DownloadJob");
    q.AddJob<DownloadJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
    .ForJob(jobKey)
    .WithIdentity("DownloadJob-trigger")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithIntervalInHours(1)
        .RepeatForever()));
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Configuração do EmailService
builder.Services.AddSingleton<EmailService>(sp => new EmailService(
    sp.GetRequiredService<ILogger<EmailService>>(),
    builder.Configuration["EmailSettings:SmtpServer"],
    int.Parse(builder.Configuration["EmailSettings:SmtpPort"]),
    builder.Configuration["EmailSettings:SmtpUsername"],
    builder.Configuration["EmailSettings:SmtpPassword"],
    builder.Configuration["EmailSettings:FromEmail"],
    builder.Configuration["EmailSettings:ToEmail"]
));

var host = builder.Build();
host.Run();
