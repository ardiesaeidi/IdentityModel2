﻿using FluentAssertions;
using IdentityModel.Client;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace IdentityModel.UnitTests
{
    public class DiscoveryPolicyTests
    {
        private NetworkHandler GetHandler(string issuer, string endpointBase = null)
        {
            if (endpointBase == null) endpointBase = issuer;

            var discoFileName = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "documents", "discovery_variable.json");
            var raw = File.ReadAllText(discoFileName);

            var document = raw.Replace("{issuer}", issuer).Replace("{endpointBase}", endpointBase);

            var jwksFileName = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "documents", "discovery_jwks.json");
            var jwks = File.ReadAllText(jwksFileName);

            var handler = new NetworkHandler(request =>
            {
                if (request.RequestUri.AbsoluteUri.EndsWith("jwks"))
                {
                    return jwks;
                }

                return document;
            }, HttpStatusCode.OK);

            return handler;
        }

        [Fact]
        public async Task connecting_to_http_should_return_error()
        {
            var client = new DiscoveryClient("http://authority");
            client.Policy.RequireHttps = true;
            client.Policy.AllowHttpOnLoopback = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.Exception);
            disco.Error.Should().StartWith("Error connecting to");
            disco.Error.Should().EndWith("HTTPS required");
        }

        [Fact]
        public async Task if_policy_allows_http_non_http_must_not_return_error()
        {
            var client = new DiscoveryClient("http://authority", GetHandler("http://authority"));
            client.Policy.RequireHttps = false;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeFalse();
        }

        [Theory]
        [InlineData("http://localhost")]
        [InlineData("http://LocalHost")]
        [InlineData("http://127.0.0.1")]
        public async Task http_on_loopback_must_not_return_error(string input)
        {
            var client = new DiscoveryClient(input, GetHandler(input));
            client.Policy.RequireHttps = true;
            client.Policy.AllowHttpOnLoopback = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeFalse();
        }

        
        [Fact]
        public async Task invalid_issuer_name_must_return_policy_error()
        {
            var handler = GetHandler("https://differentissuer");
            var client = new DiscoveryClient("https://authority", handler);
            client.Policy.ValidateIssuerName = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.PolicyViolation);
            disco.Error.Should().Be("Issuer name does not match authority");
        }

        [Fact]
        public async Task valid_issuer_name_must_return_no_error()
        {
            var handler = GetHandler("https://authority");
            var client = new DiscoveryClient("https://authority", handler);
            client.Policy.ValidateIssuerName = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeFalse();
        }

        [Fact]
        public async Task endpoints_not_using_https_should_return_policy_error()
        {
            var handler = GetHandler("https://authority", "http://authority");
            var client = new DiscoveryClient("https://authority", handler);

            client.Policy.RequireHttps = true;
            client.Policy.ValidateIssuerName = true;
            client.Policy.ValidateEndpoints = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.PolicyViolation);
            disco.Error.Should().StartWith("Endpoint does not use HTTPS");
        }

        [Theory]
        [InlineData("https://authority/sub", "https://authority")]
        [InlineData("https://authority/sub1", "https://authority/sub2")]
        public async Task endpoints_not_beneath_authority_must_return_policy_error(string authority, string endpointBase)
        {
            var handler = GetHandler(authority, endpointBase);
            var client = new DiscoveryClient(authority, handler);

            client.Policy.RequireHttps = true;
            client.Policy.ValidateIssuerName = true;
            client.Policy.ValidateEndpoints = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.PolicyViolation);
            disco.Error.Should().StartWith("Endpoint belongs to different authority");
        }

        [Theory]
        [InlineData("https://authority", "https://differentauthority")]
        [InlineData("https://authority/sub", "https://differentauthority")]
        [InlineData("https://127.0.0.1", "https://differentauthority")]
        [InlineData("https://127.0.0.1", "https://127.0.0.2")]
        [InlineData("https://127.0.0.1", "https://localhost")]
        public async Task endpoints_not_belonging_to_authority_host_must_return_policy_error(string authority, string endpointBase)
        {
            var handler = GetHandler(authority, endpointBase);
            var client = new DiscoveryClient(authority, handler);

            client.Policy.RequireHttps = true;
            client.Policy.ValidateIssuerName = true;
            client.Policy.ValidateEndpoints = true;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.PolicyViolation);
            disco.Error.Should().StartWith("Endpoint is on a different host than authority");
        }

        [Fact]
        public async Task issuer_and_endpoint_can_be_unrelated_if_allowed()
        {
            var handler = GetHandler("https://authority", "https://differentauthority");
            var client = new DiscoveryClient("https://authority", handler);

            client.Policy.RequireHttps = true;
            client.Policy.ValidateIssuerName = true;
            client.Policy.ValidateEndpoints = false;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeFalse();
        }

        [Fact]
        public async Task issuer_and_endpoint_can_be_unrelated_if_allowed_but_https_is_still_enforced()
        {
            var handler = GetHandler("https://authority", "http://differentauthority");
            var client = new DiscoveryClient("https://authority", handler);

            client.Policy.RequireHttps = true;
            client.Policy.ValidateIssuerName = true;
            client.Policy.ValidateEndpoints = false;

            var disco = await client.GetAsync();

            disco.IsError.Should().BeTrue();
            disco.Json.Should().BeNull();
            disco.ErrorType.Should().Be(ResponseErrorType.PolicyViolation);
            disco.Error.Should().StartWith("Endpoint does not use HTTPS");
        }
    }
}