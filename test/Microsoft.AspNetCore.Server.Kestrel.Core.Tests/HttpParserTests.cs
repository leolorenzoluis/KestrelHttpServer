﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines.Testing;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class HttpParserTests
    {
        [Theory]
        [MemberData(nameof(RequestLineValidData))]
        public void ParsesRequestLine(
            string requestLine,
            string expectedMethod,
            string expectedRawTarget,
            string expectedRawPath,
            string expectedDecodedPath,
            string expectedQueryString,
            string expectedVersion)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            Assert.True(parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal(requestHandler.Method, expectedMethod);
            Assert.Equal(requestHandler.Version, expectedVersion);
            Assert.Equal(requestHandler.RawTarget, expectedRawTarget);
            Assert.Equal(requestHandler.RawPath, expectedRawPath);
            Assert.Equal(requestHandler.Version, expectedVersion);
            Assert.Equal(buffer.End, consumed);
            Assert.Equal(buffer.End, examined);
        }

        [Theory]
        [MemberData(nameof(RequestLineIncompleteData))]
        public void ParseRequestLineReturnsFalseWhenGivenIncompleteRequestLines(string requestLine)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            Assert.False(parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));
        }

        [Theory]
        [MemberData(nameof(RequestLineIncompleteData))]
        public void ParseRequestLineDoesNotConsumeIncompleteRequestLine(string requestLine)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            Assert.False(parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal(buffer.Start, consumed);
            Assert.Equal(buffer.End, examined);
        }

        [Theory]
        [MemberData(nameof(RequestLineInvalidData))]
        public void ParseRequestLineThrowsOnInvalidRequestLine(string requestLine)
        {
            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);

            var parser = CreateParser(mockTrace.Object);
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal($"Invalid request line: '{requestLine.EscapeNonPrintable()}'", exception.Message);
            Assert.Equal(StatusCodes.Status400BadRequest, (exception as BadHttpRequestException).StatusCode);
        }

        [Theory]
        [MemberData(nameof(MethodWithNonTokenCharData))]
        public void ParseRequestLineThrowsOnNonTokenCharsInCustomMethod(string method)
        {
            var requestLine = $"{method} / HTTP/1.1\r\n";

            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);

            var parser = CreateParser(mockTrace.Object);
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal($"Invalid request line: '{method.EscapeNonPrintable()} / HTTP/1.1\\x0D\\x0A'", exception.Message);
            Assert.Equal(StatusCodes.Status400BadRequest, (exception as BadHttpRequestException).StatusCode);
        }

        [Theory]
        [MemberData(nameof(UnrecognizedHttpVersionData))]
        public void ParseRequestLineThrowsOnUnrecognizedHttpVersion(string httpVersion)
        {
            var requestLine = $"GET / {httpVersion}\r\n";

            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);

            var parser = CreateParser(mockTrace.Object);
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(requestLine));
            var requestHandler = new RequestHandler();

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal($"Unrecognized HTTP version: '{httpVersion}'", exception.Message);
            Assert.Equal(StatusCodes.Status505HttpVersionNotsupported, (exception as BadHttpRequestException).StatusCode);
        }

        [Theory]
        [InlineData("\r")]
        [InlineData("H")]
        [InlineData("He")]
        [InlineData("Hea")]
        [InlineData("Head")]
        [InlineData("Heade")]
        [InlineData("Header")]
        [InlineData("Header:")]
        [InlineData("Header: ")]
        [InlineData("Header: v")]
        [InlineData("Header: va")]
        [InlineData("Header: val")]
        [InlineData("Header: valu")]
        [InlineData("Header: value")]
        [InlineData("Header: value\r")]
        [InlineData("Header: value\r\n")]
        [InlineData("Header: value\r\n\r")]
        [InlineData("Header-1: value1\r\nH")]
        [InlineData("Header-1: value1\r\nHe")]
        [InlineData("Header-1: value1\r\nHea")]
        [InlineData("Header-1: value1\r\nHead")]
        [InlineData("Header-1: value1\r\nHeade")]
        [InlineData("Header-1: value1\r\nHeader")]
        [InlineData("Header-1: value1\r\nHeader-")]
        [InlineData("Header-1: value1\r\nHeader-2")]
        [InlineData("Header-1: value1\r\nHeader-2:")]
        [InlineData("Header-1: value1\r\nHeader-2: ")]
        [InlineData("Header-1: value1\r\nHeader-2: v")]
        [InlineData("Header-1: value1\r\nHeader-2: va")]
        [InlineData("Header-1: value1\r\nHeader-2: val")]
        [InlineData("Header-1: value1\r\nHeader-2: valu")]
        [InlineData("Header-1: value1\r\nHeader-2: value")]
        [InlineData("Header-1: value1\r\nHeader-2: value2")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\n\r")]
        public void ParseHeadersReturnsFalseWhenGivenIncompleteHeaders(string rawHeaders)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());

            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(rawHeaders));
            var requestHandler = new RequestHandler();
            Assert.False(parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes));
        }

        [Theory]
        [InlineData("\r")]
        [InlineData("H")]
        [InlineData("He")]
        [InlineData("Hea")]
        [InlineData("Head")]
        [InlineData("Heade")]
        [InlineData("Header")]
        [InlineData("Header:")]
        [InlineData("Header: ")]
        [InlineData("Header: v")]
        [InlineData("Header: va")]
        [InlineData("Header: val")]
        [InlineData("Header: valu")]
        [InlineData("Header: value")]
        [InlineData("Header: value\r")]
        public void ParseHeadersDoesNotConsumeIncompleteHeader(string rawHeaders)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());

            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(rawHeaders));
            var requestHandler = new RequestHandler();
            parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes);

            Assert.Equal(buffer.Start, consumed);
            Assert.Equal(buffer.End, examined);
            Assert.Equal(0, consumedBytes);
        }

        [Fact]
        public void ParseHeadersCanReadHeaderValueWithoutLeadingWhitespace()
        {
            VerifyHeader("Header", "value", "value");
        }

        [Theory]
        [InlineData("Cookie: \r\n\r\n", "Cookie", "", null, null)]
        [InlineData("Cookie:\r\n\r\n", "Cookie", "", null, null)]
        [InlineData("Cookie: \r\nConnection: close\r\n\r\n", "Cookie", "", "Connection", "close")]
        [InlineData("Cookie:\r\nConnection: close\r\n\r\n", "Cookie", "", "Connection", "close")]
        [InlineData("Connection: close\r\nCookie: \r\n\r\n", "Connection", "close", "Cookie", "")]
        [InlineData("Connection: close\r\nCookie:\r\n\r\n", "Connection", "close", "Cookie", "")]
        public void ParseHeadersCanParseEmptyHeaderValues(
            string rawHeaders,
            string expectedHeaderName1,
            string expectedHeaderValue1,
            string expectedHeaderName2,
            string expectedHeaderValue2)
        {
            var expectedHeaderNames = expectedHeaderName2 == null
                ? new[] { expectedHeaderName1 }
                : new[] { expectedHeaderName1, expectedHeaderName2 };
            var expectedHeaderValues = expectedHeaderValue2 == null
                ? new[] { expectedHeaderValue1 }
                : new[] { expectedHeaderValue1, expectedHeaderValue2 };

            VerifyRawHeaders(rawHeaders, expectedHeaderNames, expectedHeaderValues);
        }

        [Theory]
        [InlineData(" value")]
        [InlineData("  value")]
        [InlineData("\tvalue")]
        [InlineData(" \tvalue")]
        [InlineData("\t value")]
        [InlineData("\t\tvalue")]
        [InlineData("\t\t value")]
        [InlineData(" \t\tvalue")]
        [InlineData(" \t\t value")]
        [InlineData(" \t \t value")]
        public void ParseHeadersDoesNotIncludeLeadingWhitespaceInHeaderValue(string rawHeaderValue)
        {
            VerifyHeader("Header", rawHeaderValue, "value");
        }

        [Theory]
        [InlineData("value ")]
        [InlineData("value\t")]
        [InlineData("value \t")]
        [InlineData("value\t ")]
        [InlineData("value\t\t")]
        [InlineData("value\t\t ")]
        [InlineData("value \t\t")]
        [InlineData("value \t\t ")]
        [InlineData("value \t \t ")]
        public void ParseHeadersDoesNotIncludeTrailingWhitespaceInHeaderValue(string rawHeaderValue)
        {
            VerifyHeader("Header", rawHeaderValue, "value");
        }

        [Theory]
        [InlineData("one two three")]
        [InlineData("one  two  three")]
        [InlineData("one\ttwo\tthree")]
        [InlineData("one two\tthree")]
        [InlineData("one\ttwo three")]
        [InlineData("one \ttwo \tthree")]
        [InlineData("one\t two\t three")]
        [InlineData("one \ttwo\t three")]
        public void ParseHeadersPreservesWhitespaceWithinHeaderValue(string headerValue)
        {
            VerifyHeader("Header", headerValue, headerValue);
        }

        [Fact]
        public void ParseHeadersConsumesBytesCorrectlyAtEnd()
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());

            const string headerLine = "Header: value\r\n\r";
            var buffer1 = ReadableBuffer.Create(Encoding.ASCII.GetBytes(headerLine));
            var requestHandler = new RequestHandler();
            Assert.False(parser.ParseHeaders(requestHandler, buffer1, out var consumed, out var examined, out var consumedBytes));

            Assert.Equal(buffer1.Move(buffer1.Start, headerLine.Length - 1), consumed);
            Assert.Equal(buffer1.End, examined);
            Assert.Equal(headerLine.Length - 1, consumedBytes);

            var buffer2 = ReadableBuffer.Create(Encoding.ASCII.GetBytes("\r\n"));
            Assert.True(parser.ParseHeaders(requestHandler, buffer2, out consumed, out examined, out consumedBytes));

            Assert.Equal(buffer2.End, consumed);
            Assert.Equal(buffer2.End, examined);
            Assert.Equal(2, consumedBytes);
        }

        [Theory]
        [MemberData(nameof(RequestHeaderInvalidData))]
        public void ParseHeadersThrowsOnInvalidRequestHeaders(string rawHeaders, string expectedExceptionMessage)
        {
            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);

            var parser = CreateParser(mockTrace.Object);
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(rawHeaders));
            var requestHandler = new RequestHandler();

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes));

            Assert.Equal(expectedExceptionMessage, exception.Message);
            Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        }

        [Fact]
        public void ExceptionDetailNotIncludedWhenLogLevelInformationNotEnabled()
        {
            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(false);

            var parser = CreateParser(mockTrace.Object);

            // Invalid request line
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes("GET % HTTP/1.1\r\n"));
            var requestHandler = new RequestHandler();

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal("Invalid request line: ''", exception.Message);
            Assert.Equal(StatusCodes.Status400BadRequest, (exception as BadHttpRequestException).StatusCode);

            // Unrecognized HTTP version
            buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes("GET / HTTP/1.2\r\n"));

            exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined));

            Assert.Equal("Unrecognized HTTP version: ''", exception.Message);
            Assert.Equal(StatusCodes.Status505HttpVersionNotsupported, (exception as BadHttpRequestException).StatusCode);

            // Invalid request header
            buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes("Header: value\n\r\n"));

            exception = Assert.Throws<BadHttpRequestException>(() =>
                parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes));

            Assert.Equal("Invalid request header: ''", exception.Message);
            Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        }

        [Fact]
        public void ParseRequestLineSplitBufferWithoutNewLineDoesNotUpdateConsumed()
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = BufferUtilities.CreateBuffer("GET ", "/");

            var requestHandler = new RequestHandler();
            var result = parser.ParseRequestLine(requestHandler, buffer, out var consumed, out var examined);

            Assert.False(result);
            Assert.Equal(buffer.Start, consumed);
            Assert.Equal(buffer.End, examined);
        }

        private void VerifyHeader(
            string headerName,
            string rawHeaderValue,
            string expectedHeaderValue)
        {
            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes($"{headerName}:{rawHeaderValue}\r\n"));

            var requestHandler = new RequestHandler();
            parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes);

            var pairs = requestHandler.Headers.ToArray();
            Assert.Equal(1, pairs.Length);
            Assert.Equal(headerName, pairs[0].Key);
            Assert.Equal(expectedHeaderValue, pairs[0].Value);
            Assert.Equal(buffer.End, consumed);
            Assert.Equal(buffer.End, examined);
        }

        private void VerifyRawHeaders(string rawHeaders, IEnumerable<string> expectedHeaderNames, IEnumerable<string> expectedHeaderValues)
        {
            Assert.True(expectedHeaderNames.Count() == expectedHeaderValues.Count(), $"{nameof(expectedHeaderNames)} and {nameof(expectedHeaderValues)} sizes must match");

            var parser = CreateParser(Mock.Of<IKestrelTrace>());
            var buffer = ReadableBuffer.Create(Encoding.ASCII.GetBytes(rawHeaders));

            var requestHandler = new RequestHandler();
            parser.ParseHeaders(requestHandler, buffer, out var consumed, out var examined, out var consumedBytes);

            var parsedHeaders = requestHandler.Headers.ToArray();

            Assert.Equal(expectedHeaderNames.Count(), parsedHeaders.Length);
            Assert.Equal(expectedHeaderNames, parsedHeaders.Select(t => t.Key));
            Assert.Equal(expectedHeaderValues, parsedHeaders.Select(t => t.Value));
            Assert.Equal(buffer.End, consumed);
            Assert.Equal(buffer.End, examined);
        }

        private IHttpParser<RequestHandler> CreateParser(IKestrelTrace log) => new HttpParser<RequestHandler>(log);

        public static IEnumerable<string[]> RequestLineValidData => HttpParsingData.RequestLineValidData;

        public static IEnumerable<object[]> RequestLineIncompleteData => HttpParsingData.RequestLineIncompleteData.Select(requestLine => new[] { requestLine });

        public static IEnumerable<object[]> RequestLineInvalidData => HttpParsingData.RequestLineInvalidData.Select(requestLine => new[] { requestLine });

        public static IEnumerable<object[]> MethodWithNonTokenCharData => HttpParsingData.MethodWithNonTokenCharData.Select(method => new[] { method });

        public static TheoryData<string> UnrecognizedHttpVersionData => HttpParsingData.UnrecognizedHttpVersionData;

        public static IEnumerable<object[]> RequestHeaderInvalidData => HttpParsingData.RequestHeaderInvalidData;

        private class RequestHandler : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            public string Method { get; set; }

            public string Version { get; set; }

            public string RawTarget { get; set; }

            public string RawPath { get; set; }

            public string Query { get; set; }

            public bool PathEncoded { get; set; }

            public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();

            public void OnHeader(Span<byte> name, Span<byte> value)
            {
                Headers[name.GetAsciiStringNonNullCharacters()] = value.GetAsciiStringNonNullCharacters();
            }

            public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
            {
                Method = method != HttpMethod.Custom ? HttpUtilities.MethodToString(method) : customMethod.GetAsciiStringNonNullCharacters();
                Version = HttpUtilities.VersionToString(version);
                RawTarget = target.GetAsciiStringNonNullCharacters();
                RawPath = path.GetAsciiStringNonNullCharacters();
                Query = query.GetAsciiStringNonNullCharacters();
                PathEncoded = pathEncoded;
            }
        }
    }
}