﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class HeartbeatTests
    {
        [Fact]
        public void BlockedHeartbeatDoesntCauseOverlapsAndIsLoggedAsError()
        {
            var systemClock = new MockSystemClock();
            var heartbeatInterval = TimeSpan.FromMilliseconds(10);
            var heartbeatHandler = new Mock<IHeartbeatHandler>();
            var kestrelTrace = new Mock<IKestrelTrace>();
            var handlerMre = new ManualResetEventSlim();
            var traceMre = new ManualResetEventSlim();

            heartbeatHandler.Setup(h => h.OnHeartbeat(systemClock.UtcNow)).Callback(() => handlerMre.Wait());
            kestrelTrace.Setup(t => t.TimerSlow(heartbeatInterval, systemClock.UtcNow)).Callback(() => traceMre.Set());

            using (var heartbeat = new Heartbeat(new[] { heartbeatHandler.Object }, systemClock, kestrelTrace.Object, heartbeatInterval))
            {
                heartbeat.Start();
                Assert.True(traceMre.Wait(TimeSpan.FromSeconds(10)));
            }

            handlerMre.Set();

            heartbeatHandler.Verify(h => h.OnHeartbeat(systemClock.UtcNow), Times.Once());
            kestrelTrace.Verify(t => t.TimerSlow(heartbeatInterval, systemClock.UtcNow), Times.AtLeastOnce());
        }

        [Fact]
        public void ExceptionFromHeartbeatHandlerIsLoggedAsError()
        {
            var systemClock = new MockSystemClock();
            var heartbeatInterval = TimeSpan.FromMilliseconds(10);
            var heartbeatHandler = new Mock<IHeartbeatHandler>();
            var kestrelTrace = new TestKestrelTrace();
            var ex = new Exception();

            heartbeatHandler.Setup(h => h.OnHeartbeat(systemClock.UtcNow)).Throws(ex);

            using (var heartbeat = new Heartbeat(new[] { heartbeatHandler.Object }, systemClock, kestrelTrace, heartbeatInterval))
            {
                heartbeat.OnHeartbeat();
            }

            Assert.Equal(ex, kestrelTrace.Logger.Messages.Single(message => message.LogLevel == LogLevel.Error).Exception);
        }
    }
}
