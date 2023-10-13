using JasperFx.Core.Reflection;
using MQTTnet.Extensions.ManagedClient;
using Wolverine.Configuration;
using Wolverine.MQTT.Internals;

namespace Wolverine.MQTT;

public static class MqttTransportExtensions
{
    /// <summary>
    ///     Quick access to the MQTT Transport within this application.
    ///     This is for advanced usage
    /// </summary>
    /// <param name="endpoints"></param>
    /// <returns></returns>
    internal static MqttTransport MqttTransport(this WolverineOptions endpoints)
    {
        var transports = endpoints.As<WolverineOptions>().Transports;

        return transports.GetOrCreate<MqttTransport>();
    }

    /// <summary>
    /// Add a connection to an MQTT broker within this application
    /// </summary>
    /// <param name="options"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static MqttTransportExpression UseMqtt(this WolverineOptions options,
        Action<ManagedMqttClientOptionsBuilder> configure)
    {
        var transport = options.MqttTransport();
        var builder = new ManagedMqttClientOptionsBuilder();
        configure(builder);

        transport.Options = builder.Build();
        
        return new MqttTransportExpression(transport);
    }

    /// <summary>
    /// Alternative to configuring MQTT that bypasses the MQTT fluent interface
    /// </summary>
    /// <param name="options"></param>
    /// <param name="mqttOptions"></param>
    /// <returns></returns>
    public static MqttTransportExpression UseMqtt(this WolverineOptions options, ManagedMqttClientOptions mqttOptions)
    {
        var transport = options.MqttTransport();

        transport.Options = mqttOptions;
        
        return new MqttTransportExpression(transport);
    }

    /// <summary>
    /// Short hand method to use an MQTT transport connected to an MQTT broker running
    /// locally on the default port. Useful for testing scenarios
    /// </summary>
    /// <param name="options"></param>
    /// <param name="port">Optional override of the local broker port number</param>
    /// <returns></returns>
    public static MqttTransportExpression UseMqttWithLocalBroker(this WolverineOptions options, int? port = null)
    {
        return options.UseMqtt(builder =>
        {
            builder.WithClientOptions(opts =>
            {
                opts.WithTcpServer("127.0.0.1", port);
            });
        });
    }
    
    /// <summary>
    ///     Listen for incoming messages at the designated MQTT topic name
    /// </summary>
    /// <param name="endpoints"></param>
    /// <param name="topicName">The name of the Rabbit MQ queue</param>
    /// <param name="configure">
    ///     Optional configuration for this Rabbit Mq queue if being initialized by Wolverine
    ///     <returns></returns>
    public static MqttListenerConfiguration ListenToMqttTopic(this WolverineOptions endpoints, string topicName)
    {
        var transport = endpoints.MqttTransport();

        var endpoint = transport.Topics[topicName];
        endpoint.EndpointName = topicName;
        endpoint.IsListener = true;

        return new MqttListenerConfiguration(endpoint);
    }

    /// <summary>
    /// Publish messages to an MQTT topic
    /// </summary>
    /// <param name="publishing"></param>
    /// <param name="topicName"></param>
    /// <returns></returns>
    public static MqttSubscriberConfiguration ToMqttTopic(this IPublishToExpression publishing, string topicName)
    {
        var transports = publishing.As<PublishingExpression>().Parent.Transports;
        var transport = transports.GetOrCreate<MqttTransport>();

        var topic = transport.Topics[topicName];
        
        // This is necessary unfortunately to hook up the subscription rules
        publishing.To(topic.Uri);

        return new MqttSubscriberConfiguration(topic);
    }
}