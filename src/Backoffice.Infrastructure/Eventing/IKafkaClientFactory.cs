using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Backoffice.Infrastructure.Eventing;

/// <summary>
/// Thin construction seam over Confluent.Kafka so each worker builds only the client(s) it
/// needs (design.md: "Backoffice.Infrastructure — Confluent.Kafka producer/consumer").
/// </summary>
public interface IKafkaClientFactory
{
    IProducer<string, string> CreateProducer(string clientId);

    IConsumer<string, string> CreateConsumer(string groupId, string clientId);
}

public sealed class KafkaClientFactory(IOptions<KafkaSettings> settings) : IKafkaClientFactory
{
    public IProducer<string, string> CreateProducer(string clientId) =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            ClientId = clientId,
            Acks = Acks.All,
            MessageSendMaxRetries = 5,
        }).Build();

    public IConsumer<string, string> CreateConsumer(string groupId, string clientId) =>
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = groupId,
            ClientId = clientId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
}
