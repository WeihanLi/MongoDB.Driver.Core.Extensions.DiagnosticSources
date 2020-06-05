﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using MongoDB.Driver.Core.Events;
using OpenTelemetry.Adapter;
using OpenTelemetry.Trace;

namespace MongoDB.Driver.Core.Extensions.OpenTelemetry.Implementation
{
    internal class CommandListener : ListenerHandler
    {
        public CommandListener(string sourceName, Tracer tracer)
            : base(sourceName, tracer)
        {
        }

        private readonly ConcurrentDictionary<int, TelemetrySpan> _spanMap
            = new ConcurrentDictionary<int, TelemetrySpan>();

        public override void OnStartActivity(Activity activity, object payload)
        {
            if (!(payload is CommandStartedEvent message))
            {
                AdapterEventSource.Log.NullPayload("CommandListener.OnStartActivity");
                return;
            }

            Tracer.StartActiveSpanFromActivity($"mongodb.{message.CommandName}",
                activity,
                SpanKind.Client,
                out var span);

            SetSpanAttributes(span, message);

            _spanMap.TryAdd(message.RequestId, span);
        }

        public override void OnStopActivity(Activity activity, object payload)
        {
            if (!(payload is CommandSucceededEvent message))
            {
                AdapterEventSource.Log.NullPayload("CommandListener.OnStopActivity");
                return;
            }

            if (_spanMap.TryRemove(message.RequestId, out var span))
            {
                span.End();
            }
        }

        public override void OnException(Activity activity, object payload)
        {
            if (!(payload is CommandFailedEvent message))
            {
                AdapterEventSource.Log.NullPayload("CommandListener.OnExceptionActivity");
                return;
            }

            if (_spanMap.TryRemove(message.RequestId, out var span))
            {
                span.Status = Status.Unknown.WithDescription(message.Failure.Message);
                SetSpanAttributes(span, message);
                span.End();
            }
        }

        private static void SetSpanAttributes(TelemetrySpan span, CommandFailedEvent message)
        {
            span.SetAttribute("error.type", message.Failure.GetType().FullName);
            span.SetAttribute("error.msg", message.Failure.Message);
            span.SetAttribute("error.stack", message.Failure.StackTrace);
        }

        private static void SetSpanAttributes(TelemetrySpan span, CommandStartedEvent message)
        {
            span.SetAttribute("db.type", "mongo");
            span.SetAttribute("db.instance", message.DatabaseNamespace.DatabaseName);
            var endPoint = message.ConnectionId?.ServerId?.EndPoint;
            if (endPoint is IPEndPoint ipEndPoint)
            {
                span.SetAttribute("db.user", $"mongodb://{ipEndPoint.Address}:{ipEndPoint.Port}");
                span.SetAttribute("net.peer.ip", ipEndPoint.Address.ToString());
                span.SetAttribute("net.peer.port", ipEndPoint.Port);
            }
            else if (endPoint is DnsEndPoint dnsEndPoint)
            {
                span.SetAttribute("db.user", $"mongodb://{dnsEndPoint.Host}:{dnsEndPoint.Port}");
                span.SetAttribute("net.peer.name", dnsEndPoint.Host);
                span.SetAttribute("net.peer.port", dnsEndPoint.Port);
            }

            span.SetAttribute("db.statement", message.Command.ToString());
        }
    }
}