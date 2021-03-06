﻿using System;
using System.Threading.Tasks;
using Amazon;
using JustSaying.IntegrationTests.TestHandlers;
using JustSaying.Messaging.MessageHandling;
using JustSaying.TestingFramework;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace JustSaying.IntegrationTests.JustSayingFluently.MultiRegion.WithSqsPointToPointSubscriber
{
    [TestFixture]
    public class WhenAFailoverRegionIsSetup
    {
        private static readonly string PrimaryRegion = RegionEndpoint.EUWest1.SystemName;
        private static readonly string SecondaryRegion = RegionEndpoint.USEast1.SystemName;

        private readonly Future<GenericMessage> _primaryHandler = new Future<GenericMessage>();
        private readonly Future<GenericMessage> _secondaryHandler = new Future<GenericMessage>();

        private IHaveFulfilledPublishRequirements _publisher;
        private GenericMessage _message;
        private static string _activeRegion;
        private readonly Func<string> _getActiveRegion = () => _activeRegion;

        private IHaveFulfilledSubscriptionRequirements _primaryBus;
        private IHaveFulfilledSubscriptionRequirements _secondaryBus;

        [Test]
        public async Task MessagesArePublishedToTheActiveRegion()
        {
            GivenSubscriptionsToAQueueInTwoRegions();
            AndAPublisherWithAFailoverRegion();

            WhenThePrimaryRegionIsActive();
            AndAMessageIsPublished();
            await ThenTheMessageIsReceivedInThatRegion(_primaryHandler);

            WhenTheFailoverRegionIsActive();
            AndAMessageIsPublished();
            await ThenTheMessageIsReceivedInThatRegion(_secondaryHandler);

            _primaryBus.StopListening();
            _secondaryBus.StopListening();
        }

        private void GivenSubscriptionsToAQueueInTwoRegions()
        {
            var primaryHandler = Substitute.For<IHandlerAsync<GenericMessage>>();
            primaryHandler.Handle(Arg.Any<GenericMessage>()).Returns(true);
            primaryHandler
                .When(x => x.Handle(Arg.Any<GenericMessage>()))
                .Do(async x => await _primaryHandler.Complete((GenericMessage)x.Args()[0]));

            _primaryBus = CreateMeABus
                .WithLogging(new LoggerFactory())
                .InRegion(PrimaryRegion)
                .WithSqsPointToPointSubscriber()
                .IntoDefaultQueue()
                .WithMessageHandler(primaryHandler);
            _primaryBus.StartListening();

            var secondaryHandler = Substitute.For<IHandlerAsync<GenericMessage>>();
            secondaryHandler.Handle(Arg.Any<GenericMessage>()).Returns(true);
            secondaryHandler
                .When(x => x.Handle(Arg.Any<GenericMessage>()))
                .Do(async x => await _secondaryHandler.Complete((GenericMessage)x.Args()[0]));

            _secondaryBus = CreateMeABus
                .WithLogging(new LoggerFactory())
                .InRegion(SecondaryRegion)
                .WithSqsPointToPointSubscriber()
                .IntoDefaultQueue()
                .WithMessageHandler(secondaryHandler);
            _secondaryBus.StartListening();
        }

        private void AndAPublisherWithAFailoverRegion()
        {
            _publisher = CreateMeABus
                .WithLogging(new LoggerFactory())
                .InRegion(PrimaryRegion)
                .WithFailoverRegion(SecondaryRegion)
                .WithActiveRegion(_getActiveRegion)
                .WithSqsMessagePublisher<GenericMessage>(configuration => { });
        }

        private void WhenThePrimaryRegionIsActive()
        {
            _activeRegion = PrimaryRegion;
        }

        private void WhenTheFailoverRegionIsActive()
        {
            _activeRegion = SecondaryRegion;
        }

        private void AndAMessageIsPublished()
        {
            _message = new GenericMessage { Id = Guid.NewGuid() };
            _publisher.Publish(_message);
        }

        private async Task ThenTheMessageIsReceivedInThatRegion(Future<GenericMessage> handler)
        {
            var done = await Tasks.WaitWithTimeoutAsync(handler.DoneSignal);
            Assert.That(done, Is.True);

            Assert.That(handler.ReceivedMessageCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(handler.HasReceived(_message));
        }
    }
}