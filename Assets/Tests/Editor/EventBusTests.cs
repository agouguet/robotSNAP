using NUnit.Framework;
using RobotSNAP.Core;

namespace RobotSNAP.Tests.Editor
{
    public sealed class EventBusTests
    {
        private struct TestEvent
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            EventBus.Instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Instance.Clear();
        }

        [Test]
        public void Publish_NotifiesSubscribedHandler()
        {
            int received = 0;
            EventBus.Instance.Subscribe<TestEvent>(evt => received = evt.Value);

            EventBus.Instance.Publish(new TestEvent { Value = 42 });

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void Publish_AllowsHandlerToUnsubscribeItself()
        {
            int invocationCount = 0;
            System.Action<TestEvent> handler = null;
            handler = _ =>
            {
                invocationCount++;
                EventBus.Instance.Unsubscribe(handler);
            };
            EventBus.Instance.Subscribe(handler);

            EventBus.Instance.Publish(new TestEvent());
            EventBus.Instance.Publish(new TestEvent());

            Assert.That(invocationCount, Is.EqualTo(1));
        }
    }
}
