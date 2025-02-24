﻿using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using OpenIddict.Client;
using Spectre.Console;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Abstractions.OpenIddictExceptions;

#if !SUPPORTS_HOST_APPLICATION_LIFETIME
using IHostApplicationLifetime = Microsoft.Extensions.Hosting.IApplicationLifetime;
#endif

namespace OpenIddict.Sandbox.Console.Client;

public class InteractiveService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly OpenIddictClientService _service;

    public InteractiveService(
        IHostApplicationLifetime lifetime,
        OpenIddictClientService service)
    {
        _lifetime = lifetime;
        _service = service;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the host to confirm that the application has started.
        var source = new TaskCompletionSource<bool>();
        using (_lifetime.ApplicationStarted.Register(static state => ((TaskCompletionSource<bool>) state!).SetResult(true), source))
        {
            await source.Task;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var provider = await GetSelectedProviderAsync(stoppingToken);

            try
            {
                ClaimsPrincipal principal;

                // Resolve the server configuration and determine the type of flow
                // to use depending on the supported grants and the user selection.
                var configuration = await _service.GetServerConfigurationByProviderNameAsync(provider, stoppingToken);
                if (configuration.GrantTypesSupported.Contains(GrantTypes.DeviceCode) &&
                    configuration.DeviceAuthorizationEndpoint is not null &&
                    await UseDeviceAuthorizationGrantAsync(stoppingToken))
                {
                    // Ask OpenIddict to send a device authorization request and write
                    // the complete verification endpoint URI to the console output.
                    var result = await _service.ChallengeUsingDeviceAsync(new()
                    {
                        CancellationToken = stoppingToken,
                        ProviderName = provider
                    });

                    if (result.VerificationUriComplete is not null)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[yellow]Please visit [link]{result.VerificationUriComplete}[/] and confirm the displayed code is '{result.UserCode}' to complete the authentication demand.[/]");
                    }

                    else
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[yellow]Please visit [link]{result.VerificationUri}[/] and enter '{result.UserCode}' to complete the authentication demand.[/]");
                    }

                    // Wait for the user to complete the demand on the other device.
                    principal = (await _service.AuthenticateWithDeviceAsync(new()
                    {
                        DeviceCode = result.DeviceCode,
                        Interval = result.Interval,
                        ProviderName = provider,
                        Timeout = result.ExpiresIn < TimeSpan.FromMinutes(5) ? result.ExpiresIn : TimeSpan.FromMinutes(5)
                    })).Principal;
                }

                else
                {
                    AnsiConsole.MarkupLine("[cyan]Launching the system browser.[/]");

                    // Ask OpenIddict to initiate the authentication flow (typically, by starting the system browser).
                    var result = await _service.ChallengeInteractivelyAsync(new()
                    {
                        CancellationToken = stoppingToken,
                        ProviderName = provider
                    });

                    // Wait for the user to complete the authorization process.
                    principal = (await _service.AuthenticateInteractivelyAsync(new()
                    {
                        Nonce = result.Nonce
                    })).Principal;
                }

                AnsiConsole.MarkupLine("[green]Authentication successful:[/]");

                var table = new Table()
                    .AddColumn(new TableColumn("Claim type").Centered())
                    .AddColumn(new TableColumn("Claim value type").Centered())
                    .AddColumn(new TableColumn("Claim value").Centered());

                foreach (var claim in principal.Claims)
                {
                    table.AddRow(
                        claim.Type.EscapeMarkup(),
                        claim.ValueType.EscapeMarkup(),
                        claim.Value.EscapeMarkup());
                }

                AnsiConsole.Write(table);
            }

            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[red]The authentication process was aborted.[/]");
            }

            catch (ProtocolException exception) when (exception.Error is Errors.AccessDenied)
            {
                AnsiConsole.MarkupLine("[yellow]The authorization was denied by the end user.[/]");
            }

            catch
            {
                AnsiConsole.MarkupLine("[red]An error occurred while trying to authenticate the user.[/]");
            }
        }

        static Task<bool> UseDeviceAuthorizationGrantAsync(CancellationToken cancellationToken)
        {
            static bool Prompt() => AnsiConsole.Prompt(new ConfirmationPrompt(
                "Would you like to authenticate using the device authorization grant?")
            {
                DefaultValue = false,
                ShowDefaultValue = true
            });

            return WaitAsync(Task.Run(Prompt, cancellationToken), cancellationToken);
        }

        Task<string> GetSelectedProviderAsync(CancellationToken cancellationToken)
        {
            async Task<string> PromptAsync() => AnsiConsole.Prompt(new SelectionPrompt<OpenIddictClientRegistration>()
                .Title("Select the authentication provider you'd like to log in with.")
                .AddChoices(from registration in await _service.GetClientRegistrationsAsync(stoppingToken)
                            where !string.IsNullOrEmpty(registration.ProviderName)
                            where !string.IsNullOrEmpty(registration.ProviderDisplayName)
                            select registration)
                .UseConverter(registration => registration.ProviderDisplayName!)).ProviderName!;

            return WaitAsync(Task.Run(PromptAsync, cancellationToken), cancellationToken);
        }

        static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
#if SUPPORTS_TASK_WAIT_ASYNC
            return await task.WaitAsync(cancellationToken);
#else
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.None);

            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>) state!).SetResult(true), source))
            {
                if (await Task.WhenAny(task, source.Task) == source.Task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return await task;
            }
#endif
        }
    }
}
