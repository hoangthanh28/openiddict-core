﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Extensions;

namespace OpenIddict.Validation;

/// <summary>
/// Contains the methods required to ensure that the OpenIddict validation configuration is valid.
/// </summary>
public sealed class OpenIddictValidationConfiguration : IPostConfigureOptions<OpenIddictValidationOptions>
{
    private readonly OpenIddictValidationService _service;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictValidationConfiguration"/> class.
    /// </summary>
    /// <param name="service">The validation service.</param>
    public OpenIddictValidationConfiguration(OpenIddictValidationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenIddictValidationOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.JsonWebTokenHandler is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0075));
        }

        if (options.Configuration is null && options.ConfigurationManager is null &&
            options.Issuer is null && options.ConfigurationEndpoint is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0128));
        }

        if (options.Issuer is not null)
        {
            if (!options.Issuer.IsAbsoluteUri || !options.Issuer.IsWellFormedOriginalString())
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0136));
            }

            if (!string.IsNullOrEmpty(options.Issuer.Fragment) || !string.IsNullOrEmpty(options.Issuer.Query))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0137));
            }
        }

        if (options.ValidationType is OpenIddictValidationType.Introspection)
        {
            if (!options.Handlers.Exists(static descriptor => descriptor.ContextType == typeof(ApplyIntrospectionRequestContext)))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0129));
            }

            if (options.Issuer is null && options.ConfigurationEndpoint is null)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0130));
            }

            if (string.IsNullOrEmpty(options.ClientId))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0131));
            }

            if (options.SigningCredentials.Count is 0 && string.IsNullOrEmpty(options.ClientSecret))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0132));
            }

            if (options.EnableAuthorizationEntryValidation)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0133));
            }

            if (options.EnableTokenEntryValidation)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0134));
            }
        }

        // If all the registered encryption credentials are backed by a X.509 certificate, at least one of them must be valid.
        if (options.EncryptionCredentials.Count is not 0 &&
            options.EncryptionCredentials.TrueForAll(credentials => credentials.Key is X509SecurityKey x509SecurityKey &&
                (x509SecurityKey.Certificate.NotBefore > DateTime.Now || x509SecurityKey.Certificate.NotAfter < DateTime.Now)))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0087));
        }

        if (options.ConfigurationManager is null)
        {
            if (options.Configuration is not null)
            {
                if (options.Configuration.Issuer is not null &&
                    options.Configuration.Issuer != options.Issuer)
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0394));
                }

                // Note: the issuer may be null. In this case, it will be usually provided by
                // a validation handler registered by the host (e.g ASP.NET Core or OWIN/Katana).
                options.Configuration.Issuer ??= options.Issuer;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIddictConfiguration>(options.Configuration);
            }

            else
            {
                if (!options.Handlers.Exists(static descriptor => descriptor.ContextType == typeof(ApplyConfigurationRequestContext)) ||
                    !options.Handlers.Exists(static descriptor => descriptor.ContextType == typeof(ApplyCryptographyRequestContext)))
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0135));
                }

                options.ConfigurationEndpoint = OpenIddictHelpers.CreateAbsoluteUri(
                    options.Issuer,
                    options.ConfigurationEndpoint ?? new Uri(".well-known/openid-configuration", UriKind.Relative));

                options.ConfigurationManager = new ConfigurationManager<OpenIddictConfiguration>(
                    options.ConfigurationEndpoint.AbsoluteUri, new OpenIddictValidationRetriever(_service))
                {
                    AutomaticRefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultAutomaticRefreshInterval,
                    RefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultRefreshInterval
                };
            }
        }

        // Sort the handlers collection using the order associated with each handler.
        options.Handlers.Sort((left, right) => left.Order.CompareTo(right.Order));

        // Sort the encryption and signing credentials.
        options.EncryptionCredentials.Sort((left, right) => Compare(left.Key, right.Key));
        options.SigningCredentials.Sort((left, right) => Compare(left.Key, right.Key));

        // Attach the encryption credentials to the token validation parameters.
        options.TokenValidationParameters.TokenDecryptionKeys =
            from credentials in options.EncryptionCredentials
            select credentials.Key;

        static int Compare(SecurityKey left, SecurityKey right) => (left, right) switch
        {
            // If the two keys refer to the same instances, return 0.
            (SecurityKey first, SecurityKey second) when ReferenceEquals(first, second) => 0,

            // If one of the keys is a symmetric key, prefer it to the other one.
            (SymmetricSecurityKey, SymmetricSecurityKey) => 0,
            (SymmetricSecurityKey, SecurityKey) => -1,
            (SecurityKey, SymmetricSecurityKey) => 1,

            // If one of the keys is backed by a X.509 certificate, don't prefer it if it's not valid yet.
            (X509SecurityKey first, SecurityKey)  when first.Certificate.NotBefore  > DateTime.Now => 1,
            (SecurityKey, X509SecurityKey second) when second.Certificate.NotBefore > DateTime.Now => -1,

            // If the two keys are backed by a X.509 certificate, prefer the one with the furthest expiration date.
            (X509SecurityKey first, X509SecurityKey second) => -first.Certificate.NotAfter.CompareTo(second.Certificate.NotAfter),

            // If one of the keys is backed by a X.509 certificate, prefer the X.509 security key.
            (X509SecurityKey, SecurityKey) => -1,
            (SecurityKey, X509SecurityKey) => 1,

            // If the two keys are not backed by a X.509 certificate, none should be preferred to the other.
            (SecurityKey, SecurityKey) => 0
        };
    }
}
