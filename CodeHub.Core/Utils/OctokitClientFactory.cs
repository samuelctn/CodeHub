﻿using System;
using Octokit.Internal;
using Splat;
using Octokit;
using System.Net.Http;
using CodeHub.Core.Services;

namespace CodeHub.Core.Utilities
{
    public static class OctokitClientFactory
    {
        public static Func<HttpClientHandler> CreateMessageHandler = () => new HttpClientHandler();
        public static readonly string[] Scopes = { "user", "repo", "gist", "notifications" };

        public static GitHubClient Create(Uri domain, Credentials credentials)
        {
            var networkActivityService = Locator.Current.GetService<INetworkActivityService>();
            var client = new HttpClientAdapter(CreateMessageHandler);
            var httpClient = new OctokitNetworkClient(client, networkActivityService);

            var connection = new Connection(
                new ProductHeaderValue("CodeHub"),
                domain,
                new InMemoryCredentialStore(credentials),
                httpClient,
                new SimpleJsonSerializer());
            return new GitHubClient(connection);
        }
    }
}

