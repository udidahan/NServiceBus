using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using NServiceBus.Unicast.Config;
using NServiceBus.Unicast.Subscriptions.Msmq;

namespace NServiceBus.Host.Internal
{
    public class ConfigurationBuilder
    {
        public Configure BuildConfigurationFrom(IConfigureThisEndpoint specifier, Type endpointType)
        {
            Configure busConfiguration;

            if (specifier is ISpecify.TypesToScan)
                busConfiguration = Configure.With((specifier as ISpecify.TypesToScan).TypesToScan);
            else
                if (specifier is ISpecify.AssembliesToScan)
                    busConfiguration = Configure.With(new List<Assembly>((specifier as ISpecify.AssembliesToScan).AssembliesToScan).ToArray());
                else
                    if (specifier is ISpecify.ProbeDirectory)
                        busConfiguration = Configure.With((specifier as ISpecify.ProbeDirectory).ProbeDirectory);
                    else
                        busConfiguration = Configure.With();

            IContainer container = null;


            if (specifier is ISpecify.ContainerInstanceToUse)
                container = (specifier as ISpecify.ContainerInstanceToUse).ContainerInstance;

            Type containerType = null;
            Type messageEndpointType = null;

            foreach (var t in endpointType.GetInterfaces())
            {
                var args = t.GetGenericArguments();
                if (args.Length == 1)
                {
                    if (typeof(IContainer).IsAssignableFrom(args[0]))
                        if (typeof(ISpecify.ContainerTypeToUse<>).MakeGenericType(args[0]).IsAssignableFrom(endpointType))
                            containerType = args[0];

                    if (typeof(IMessageEndpoint).IsAssignableFrom(args[0]))
                        if (typeof(ISpecify.ToRun<>).MakeGenericType(args[0]).IsAssignableFrom(endpointType))
                            messageEndpointType = args[0];
                }
            }

            if (container != null)
            {
                ObjectBuilder.Common.Config.ConfigureCommon.With(busConfiguration, container);
            }
            else if (containerType != null)
                ObjectBuilder.Common.Config.ConfigureCommon.With(
                    busConfiguration,
                    Activator.CreateInstance(containerType) as IContainer
                    );
            else
                busConfiguration.SpringBuilder();

            if (specifier is As.aClient && specifier is As.aServer)
                throw new InvalidOperationException("Cannot specify endpoint both as a client and as a server.");

            ConfigUnicastBus configUnicastBus = null;
           
            if (specifier is As.aPublisher && !(specifier is As.aServer))
                throw new ConfigurationErrorsException("Enpoints that is specified to be publishers must be servers as well, please implement As.aServer!");

            if (specifier is As.aClient)
                configUnicastBus = busConfiguration
                    .MsmqTransport()
                    .IsTransactional(false)
                    .PurgeOnStartup(true)
                    .UnicastBus()
                    .ImpersonateSender(false);

            if (specifier is As.aServer)
            {
                configUnicastBus = busConfiguration
                    .MsmqTransport()
                    .IsTransactional(true)
                    .PurgeOnStartup(false)
                    .Sagas()
                    .UnicastBus()
                    .ImpersonateSender(true);

                if (!(specifier is ISpecify.MyOwnSagaPersistence))
                    busConfiguration.Configurer.ConfigureComponent<InMemorySagaPersister>(ComponentCallModelEnum.Singleton);

                if (specifier is As.aPublisher)
                {
                    var subscriptionConfig =
                        Configure.GetConfigSection<Config.DbSubscriptionStorageConfig>();

                    if (subscriptionConfig == null)
                    {
                        string q = Program.GetEndpointId(endpointType) + "_subscriptions";
                        busConfiguration.Configurer.ConfigureComponent<MsmqSubscriptionStorage>(ComponentCallModelEnum.Singleton)
                            .ConfigureProperty(s => s.Queue, q);
                    }
                    else
                        busConfiguration.DbSubscriptionStorage();
                }
            }

            if (configUnicastBus != null)
            {
                if (specifier is ISpecify.MessageHandlerOrdering)
                    (specifier as ISpecify.MessageHandlerOrdering).SpecifyOrder(new Order { config = configUnicastBus });
                else
                    configUnicastBus.LoadMessageHandlers();

                if (specifier is IDontWantToSubscribeAutomatically)
                    configUnicastBus.DoNotAutoSubscribe();
            }

            if (specifier is ISpecify.ToUseXmlSerialization)
            {
                if (specifier is ISpecify.XmlSerializationNamespace)
                    busConfiguration.XmlSerializer((specifier as ISpecify.XmlSerializationNamespace).Namespace);
                else
                    busConfiguration.XmlSerializer();
            }
            else
                if (!(specifier is ISpecify.MyOwnSerialization))
                    busConfiguration.BinarySerializer();

            if (specifier is IWantCustomInitialization)
                (specifier as IWantCustomInitialization).Init(busConfiguration);

            if (messageEndpointType != null)
                Configure.TypeConfigurer.ConfigureComponent(messageEndpointType, ComponentCallModelEnum.Singleton);
            return busConfiguration;           
        }
    }
}