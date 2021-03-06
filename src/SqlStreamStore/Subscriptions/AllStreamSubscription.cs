﻿namespace SqlStreamStore.Subscriptions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using SqlStreamStore.Infrastructure;
    using SqlStreamStore.Streams;
    using SqlStreamStore;

    public sealed class AllStreamSubscription : SubscriptionBase, IAllStreamSubscription
    {
        private long _nextPosition;

        public AllStreamSubscription(
            long? fromPosition,
            IReadonlyStreamStore readonlyStreamStore,
            IObservable<Unit> streamStoreAppendedNotification,
            StreamMessageReceived streamMessageReceived,
            SubscriptionDropped subscriptionDropped = null,
            string name = null)
            :base(readonlyStreamStore, streamStoreAppendedNotification, streamMessageReceived, subscriptionDropped, name)
        {
            FromPosition = fromPosition;
            LastPosition = fromPosition;
            _nextPosition = fromPosition + 1 ?? Position.Start;
        }

        public long? FromPosition { get; }

        public long? LastPosition { get; private set; }

        public override async Task Start(CancellationToken cancellationToken)
        {
            if(FromPosition == Position.End)
            {
                // Get the last stream version and subscribe from there.
                var eventsPage = await ReadonlyStreamStore.ReadAllBackwards(
                    Position.End,
                    1,
                    cancellationToken).NotOnCapturedContext();

                // If fromPosition = 0, we have empty store, so start from zero, otherwise, the next position is 
                // one after the FromPosition.
                _nextPosition = eventsPage.FromPosition == 0 ?  0 : eventsPage.FromPosition + 1;
            }
            await base.Start(cancellationToken).NotOnCapturedContext();
        }

        protected override async Task<bool> DoFetch()
        {
            var allMessagesPage = await ReadonlyStreamStore
                .ReadAllForwards(
                    _nextPosition,
                    PageSize,
                    IsDisposed)
                .NotOnCapturedContext();
            bool isEnd = allMessagesPage.IsEnd;
            
            foreach(var streamMessage in allMessagesPage.Messages)
            {
                if(IsDisposed.IsCancellationRequested)
                {
                    return true;
                }
                try
                {
                    await StreamMessageReceived(streamMessage).NotOnCapturedContext();
                    LastPosition = streamMessage.Position;
                    _nextPosition = streamMessage.Position + 1;
                }
                catch(Exception ex)
                {
                    try
                    {
                        SubscriptionDropped.Invoke(ex.Message, ex);
                    }
                    catch
                    {
                        //TODO logging
                    }
                    finally
                    {
                        Dispose();
                    }
                }
            }
            
            return isEnd;
        }
    }
}
