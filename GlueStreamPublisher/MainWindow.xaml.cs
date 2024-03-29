﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using DOT.AGM.Core.Server;
using DOT.AGM.Extensions;
using DOT.AGM.Server;
using DOT.Core.Extensions;
using Tick42;

namespace GlueStreamPublisher
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml.
    ///     This demonstrates the creation of a Glue42 stream with branches and publishing images and data.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        ///     Used for Glue42 streaming branches
        /// </summary>
        public enum StoreDepartment
        {
            None,
            Books,
            Apparel,
            Food
        }

        private readonly Glue42 glue_;
        private readonly IServerEventStream stream_;
        private readonly Dictionary<StoreDepartment, Timer> timer_ = new Dictionary<StoreDepartment, Timer>();

        public MainWindow()
        {
            InitializeComponent();

            // create a timer for each of the StoreDepartments (branches)
            // each timer will push to the relevant branch
            timer_.Add(StoreDepartment.Books, new Timer(OnTimerTick, StoreDepartment.Books, 0, 1000));
            timer_.Add(StoreDepartment.Apparel, new Timer(OnTimerTick, StoreDepartment.Apparel, 0, 2500));
            timer_.Add(StoreDepartment.Food, new Timer(OnTimerTick, StoreDepartment.Food, 0, 5000));

            // initialize Glue
            glue_ = new Glue42();

            // give the publisher an application name
            // explicitly turn off Glue Window management, Glue contexts, Glue AppManager, Glue metrics and Glue notifications
            // as they're not used in this example
            glue_.Initialize("GlueStreamsPublisher", useAppManager: false, useStickyWindows: false,
                useContexts: false, useMetrics: false, useNotifications: false);

            // detect connection status change and log it to the UI
            glue_.Interop.ConnectionStatusChanged += (sender, args) => LogMessage($"Glue is now {args.Status}");

            // register a streaming endpoint (Glue stream) called GlueDemoStoreStream

            stream_ = glue_.Interop.RegisterStreamingEndpoint(smb => smb.SetMethodName("GlueDemoStoreStream")
                    .SetParameterSignature("bool reject, int storeDepartment"),
                new ServerEventStreamHandler(false)
                {
                    // in each of the stream handler events we have the optional cookie parameter
                    // that is optionally supplied while registering the stream

                    // this lambda is invoked when a new subscription request is received
                    SubscriptionRequestHandler = (stream, request, cookie) =>
                    {
                        // validate the request
                        // for demo purposes we have a reject argument sent by the subscriber
                        bool reject = request.SubscriptionContext.Arguments.GetValueByName("reject", v => v.AsBool);

                        StoreDepartment department = request.SubscriptionContext.Arguments.GetValueByName("StoreDepartment",
                            v => Enum.TryParse(v, true, out StoreDepartment sd) ? sd : StoreDepartment.None);

                        LogMessage($@"Subscription request from {request.Caller} for StoreDepartment {department}: {request.SubscriptionContext.Arguments.AsString()}");

                        if (reject || department == StoreDepartment.None)
                        {
                            var rejectionMsg = new StringBuilder($"Subscription request from {request.Caller} rejected. Reason(s): ");
                            if (reject)
                            {
                                rejectionMsg.Append("Reject requested;");
                            }
                            if (department == StoreDepartment.None)
                            {
                                rejectionMsg.Append("StoreDepartment invalid");
                            }

                            LogMessage(rejectionMsg.ToString());

                            // if we cannot validate the subscription request we can reject it
                            request.Reject(smrb =>
                                smrb.SetMessage(rejectionMsg.ToString())
                                    .SetContext(cb =>
                                        cb.AddValue("RejectionArgument", new[] {5, 3, 2, 1, 2})
                                            .AddValue("MoreRejectionArguments", "sdfgsdfg")).Build());

                            return null;
                        }

                        // when we validate the subscription request, we can assign the subscriber to a branch
                        // in this demo we will use the StoreDepartment as a branch (https://docs.glue42.com/g4e/net-interop/index.html#interop-streaming)

                        // branches are an optional grouping of multiple subscribers to ease data category segregation - the branches are keyed by
                        // 'any' object supplied by the publisher

                        // if we don't need a branching mechanism, we then use the 'main' branch by *not* supplying value for branch key.
                        return request.Accept(smrb => smrb.Build(), branchKey: department);
                    },

                    // this lambda is invoked when new subscriber has been connected to a branch
                    SubscriberHandler = (stream, subscriber, branch, cookie) =>
                    {
                        IEventStreamSubscriptionRequest request = subscriber.Subscription;
                        // log the subscriber
                        LogMessage(
                            $"New subscriber {request.Caller} {request.SubscriptionContext.Arguments.AsString()}");

                        // push an 'image' to that subscriber which is received *only* by it (last image pattern)

                        // this will be received as an OOB data - see the demo subscriber code
                        subscriber.Push(cb =>
                            cb.AddValue("SubscribersAsImage", glue_.AGMObjectSerializer.Serialize(stream.GetBranches()
                                .SelectMany(b =>
                                    b.GetSubscribers().Select(sb => sb.Subscription.Caller)))));

                        // e.g. keep the subscriptions in a list
                        DispatchAction(() => Subscriptions.Items.Add(request.Caller.ApplicationName +
                                                                     " " +
                                                                     request.SubscriptionContext
                                                                         .Arguments
                                                                         .AsString()));
                    },

                    //this lambda is invoked when a subscriber cancels its subscription or has been kicked (banned) by the publisher
                    UnsubscribeHandler = (stream, subscriber, unsubscribeCxt, branch, cookie) =>
                    {
                        IEventStreamSubscriptionRequest request = subscriber.Subscription;

                        // log the subscription cancellation
                        LogMessage(
                            $"Removed subscriber {request.Caller} {request.SubscriptionContext.Arguments.AsString()}");

                        // remove subscriptions when cancelled
                        DispatchAction(() =>
                            Subscriptions.Items.Remove(request.Caller.ApplicationName + " " +
                                                       request.SubscriptionContext.Arguments.AsString()));
                    }
                });
        }

        private void DispatchAction(Action action)
        {
            Dispatcher?.BeginInvoke(action);
        }

        private void LogMessage(string message)
        {
            DispatchAction(() => Log.Items.Add(message));
        }

        private void OnTimerTick(object state)
        {
            // in here the state plays the role of 'updated branch'
            if (!(state is StoreDepartment sd) || stream_ == null || !stream_.TryGetBranch(out IEventStreamBranch branch, sd))
            {
                return;
            }

            // branch.GetSubscribers() - a snapshot of the current subscribers to that branch

            // push data to that branch only
            branch.Push(cb => cb.AddValue("StoreDepartment", sd.ToString()).AddValue("Time", DateTime.UtcNow.ToString()));
        }
    }
}