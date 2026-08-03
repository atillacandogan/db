using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqConnectionFactory(IConfiguration configuration)
{
    public ConnectionFactory Create()
    {
        var connectionString = configuration.GetConnectionString("rabbitmq");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new ConnectionFactory
            {
                Uri = new Uri(connectionString)
            };
        }

        return new ConnectionFactory
        {
            HostName = configuration["RABBITMQ_HOST"] ?? "localhost",
            Port = int.TryParse(configuration["RABBITMQ_PORT"], out var port) ? port : 5672,
            UserName = configuration["RABBITMQ_USERNAME"] ?? "guest",
            Password = configuration["RABBITMQ_PASSWORD"] ?? "guest"
        };
    }
}
