using Quartz;
using Tijuquinha;
using Tijuquinha.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuração do Quartz
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("DownloadJob");
    q.AddJob<DownloadJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("DownloadJob-trigger")
        .StartNow()
        .WithCronSchedule("0 0 6 ? * TUE") // Toda terça-feira às 6h da manhã
    );
});

// Registra o Quartz como Hosted Service (já injeta a JobFactory correta)
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Configuração do EmailService
builder.Services.AddSingleton(sp => new EmailService(
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
