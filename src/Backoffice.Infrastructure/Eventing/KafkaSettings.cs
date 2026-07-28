namespace Backoffice.Infrastructure.Eventing;

/// <summary>Bound from configuration section "Kafka" — matches
/// contracts/messaging/topology.yaml's topic/consumer-group naming.</summary>
public sealed class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string EventsTopic { get; set; } = "backoffice.events.v1";
    public string DlqTopic { get; set; } = "backoffice.dlq.v1";
    public string ConsumerGroup { get; set; } = "backoffice-workflow-v1";
}
