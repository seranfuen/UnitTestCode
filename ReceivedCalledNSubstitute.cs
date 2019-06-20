using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace UnitTestCode
{
    [TestFixture]
    public class ReceivedCalledNSubstitute
    {
        /// <summary>
        ///     Anotated like this, NUnit will call this method every time a new test is run so that we can
        ///     neatly separate the environment given to each unit test
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _mockedRepository = Substitute.For<IPurchaseOrderRepository>();
            // Return exactly this PO whenever we call this method on the repository with argument 100
            // otherwise return null
            _mockedRepository.GetPurchaseOrder(100).Returns(new PurchaseOrder
            {
                Id = 100,
                Currency = "USD",
                Quantity = 500,
                Total = 1200,
                CustomerId = 5
            });

            _mockedSessionData = Substitute.For<ISessionData>();
            // Always return 5550
            _mockedSessionData.CurrentSessionId.Returns(5550);

            // No need to mock anything else, as it only received in our example events
            _mockedBus = Substitute.For<IEventBus>();
        }

        private IPurchaseOrderRepository _mockedRepository;
        private IEventBus _mockedBus;
        private ISessionData _mockedSessionData;

        private static bool PurchaseOrderCancelInfoIsExpected(PurchaseOrderCanceledInfo purchaseOrderCanceledInfo)
        {
            purchaseOrderCanceledInfo.PurchaseOrderId.Should().Be(100);
            purchaseOrderCanceledInfo.CustomerId.Should().Be(5);
            purchaseOrderCanceledInfo.Amount.Should().Be(1200);
            purchaseOrderCanceledInfo.SessionId.Should().Be(5550);
            purchaseOrderCanceledInfo.Timestamp.Should().BeSameDateAs(DateTime.Now);

            // We will not reach this return statement if one of the assertions earlier fail and throw an exception
            return true;
        }

        [Test]
        public void Cancel_calls_event_bus_lambda()
        {
            var entityUnderTest = new PurchaseOrderController(_mockedRepository, _mockedBus, _mockedSessionData);
            entityUnderTest.Cancel(100);

            _mockedBus.Received(1).PushPurchaseOrderCanceledEvent(Arg.Is<PurchaseOrderCanceledInfo>(x =>
                x.PurchaseOrderId == 100 &&
                x.CustomerId == 5 &&
                x.Amount == 1200 &&
                x.SessionId == 5550 &&
                x.Timestamp.Date == DateTime.Now.Date));
        }


        [Test]
        public void Cancel_calls_event_bus_with_assertions()
        {
            var entityUnderTest = new PurchaseOrderController(_mockedRepository, _mockedBus, _mockedSessionData);
            entityUnderTest.Cancel(100);

            _mockedBus.Received(1)
                .PushPurchaseOrderCanceledEvent(
                    Arg.Is<PurchaseOrderCanceledInfo>(x => PurchaseOrderCancelInfoIsExpected(x)));
        }

        [Test]
        public void Cancel_saves_PurchaseOrder()
        {
            var entityUnderTest = new PurchaseOrderController(_mockedRepository, _mockedBus, _mockedSessionData);
            entityUnderTest.Cancel(100);

            // NSubstitute checks its mocked object and throws an exception if no such a call was received
            // If the lambda expression is evaluated to true, it will be successful, otherwise an exception is thrown
            _mockedRepository.Received(1).SavePurchaseOrder(Arg.Is<PurchaseOrder>(x => x.Canceled && x.Id == 100));
        }
    }

    public class PurchaseOrderController
    {
        private readonly IEventBus _bus;
        private readonly IPurchaseOrderRepository _purchaseOrderRepository;
        private readonly ISessionData _sessionData;

        public PurchaseOrderController(IPurchaseOrderRepository purchaseOrderRepository, IEventBus bus,
            ISessionData sessionData)
        {
            // Here we inject the dependencies. We can use mocks, or we can have a DI (Dependency Injection framework) resolve them
            // for us in the actual code. NInject, or ASP .NET CORE can handle this

            // Throwing an exception as soon as I recognize a null argument when I'm not willing to accept it is the way I always handle
            // this type of injection so I can easily detect, as soon as possible, the error. If I did not do this,
            // the controller would instantiate properly, and only later we'd get the error - for example, if we pass a null bus always
            // but we do not normally call an action using the bus, it could go unnoticed for some time
            _purchaseOrderRepository = purchaseOrderRepository ??
                                       throw new ArgumentNullException(nameof(purchaseOrderRepository));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _sessionData = sessionData ?? throw new ArgumentNullException(nameof(sessionData));
        }

        public void Cancel(int purchaseOrderId)
        {
            var purchaseOrderToCancel = _purchaseOrderRepository.GetPurchaseOrder(purchaseOrderId);

            // My policy is to have very limited tolerance towards accepting null arguments
            // However, returning null can be acceptable, and sometimes it's the only way (I don't like the idea of special value objects to substitute null)
            // How to handle the null depends on the use case. In this case I choose to silently return, but I might log an incidence, throw an exception,
            // redirect to another place, etc
            if (purchaseOrderToCancel == null) return;

            purchaseOrderToCancel.Canceled = true;
            _purchaseOrderRepository.SavePurchaseOrder(purchaseOrderToCancel);

            _bus.PushPurchaseOrderCanceledEvent(new PurchaseOrderCanceledInfo
            {
                CustomerId = purchaseOrderToCancel.CustomerId,
                Amount = purchaseOrderToCancel.Quantity,
                PurchaseOrderId = purchaseOrderToCancel.Id,
                Timestamp = DateTimeOffset.Now,
                SessionId = _sessionData.CurrentSessionId
            });
        }
    }

    public interface IEventBus
    {
        void PushPurchaseOrderCanceledEvent(PurchaseOrderCanceledInfo poCanceledInfo);
    }

    public class PurchaseOrderCanceledInfo
    {
        public int PurchaseOrderId { get; set; }
        public int CustomerId { get; set; }

        public decimal Amount { get; set; }

        public int SessionId { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }

    public interface IPurchaseOrderRepository
    {
        PurchaseOrder GetPurchaseOrder(int purchaseOrderId);
        void SavePurchaseOrder(PurchaseOrder purchaseOrder);
    }

    public class PurchaseOrder
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public decimal Total { get; set; }
        public int Quantity { get; set; }
        public string Currency { get; set; }
        public bool Canceled { get; set; }
    }

    public interface ISessionData
    {
        int CurrentSessionId { get; }
    }
}