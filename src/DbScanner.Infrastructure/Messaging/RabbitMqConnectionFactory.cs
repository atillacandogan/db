using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using DbScanner.Infrastructure.Identity;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqConnectionFactory(IConfiguration configuration)
{
    public ConnectionFactory Create()
    {
        var connectionString = configuration.GetConnectionString("rabbitmq");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString)
            };
            var tenant = configuration.GetSection(TenantIsolationOptions.SectionName).Get<TenantIsolationOptions>() ?? new();
            if (tenant.DedicatedTenant && (factory.VirtualHost != $"tenant.{tenant.TenantId}"
                || factory.UserName == "guest"))
                throw new InvalidOperationException("Dedicated tenant requires its own RabbitMQ vhost tenant.<TenantId> and non-guest login with permissions restricted to that vhost.");
            return factory;
        }

        if (configuration.GetValue<bool>("TenantIsolation:DedicatedTenant"))
            throw new InvalidOperationException("Dedicated tenant requires an explicit RabbitMQ connection string with a tenant vhost.");
        return new ConnectionFactory
        {
            HostName = configuration["RABBITMQ_HOST"] ?? "localhost",
            Port = int.TryParse(configuration["RABBITMQ_PORT"], out var port) ? port : 5672,
            UserName = configuration["RABBITMQ_USERNAME"] ?? "guest",
            Password = configuration["RABBITMQ_PASSWORD"] ?? "guest"
        };
    }
}
