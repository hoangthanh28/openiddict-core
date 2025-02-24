﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Extensions;

namespace OpenIddict.Client;

/// <summary>
/// Contains the methods required to ensure that the OpenIddict client configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class OpenIddictClientConfiguration : IPostConfigureOptions<OpenIddictClientOptions>
{
    private readonly OpenIddictClientService _service;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictClientConfiguration"/> class.
    /// </summary>
    /// <param name="service">The OpenIddict client service.</param>
    public OpenIddictClientConfiguration(OpenIddictClientService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenIddictClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.JsonWebTokenHandler is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0075));
        }

        foreach (var registration in options.Registrations)
        {
            if (registration.Issuer is null)
            {
                throw string.IsNullOrEmpty(registration.ProviderType) ?
                    new InvalidOperationException(SR.GetResourceString(SR.ID0405)) :
                    new InvalidOperationException(SR.GetResourceString(SR.ID0411));
            }

            if (!registration.Issuer.IsAbsoluteUri || !registration.Issuer.IsWellFormedOriginalString())
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0136));
            }

            if (!string.IsNullOrEmpty(registration.Issuer.Fragment) || !string.IsNullOrEmpty(registration.Issuer.Query))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0137));
            }

            // If no explicit registration identifier was set, compute a stable
            // hash based on the issuer URI and the provider name, if available.
            if (string.IsNullOrEmpty(registration.RegistrationId))
            {
                using var algorithm = CryptoConfig.CreateFromName("OpenIddict SHA-256 Cryptographic Provider") switch
                {
                    SHA256 result => result,
                    null => SHA256.Create(),
                    var result => throw new CryptographicException(SR.FormatID0351(result.GetType().FullName))
                };

                TransformBlock(algorithm, registration.Issuer.AbsoluteUri);

                if (!string.IsNullOrEmpty(registration.ProviderName))
                {
                    TransformBlock(algorithm, registration.ProviderName);
                }

                algorithm.TransformFinalBlock([], 0, 0);

                registration.RegistrationId = Base64UrlEncoder.Encode(algorithm.Hash);
            }

            if (registration.ConfigurationManager is null)
            {
                if (registration.Configuration is not null)
                {
                    if (registration.Configuration.Issuer is not null &&
                        registration.Configuration.Issuer != registration.Issuer)
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0395));
                    }

                    registration.Configuration.Issuer ??= registration.Issuer;
                    registration.ConfigurationManager = new StaticConfigurationManager<OpenIddictConfiguration>(registration.Configuration);
                }

                else
                {
                    if (!options.Handlers.Exists(static descriptor => descriptor.ContextType == typeof(ApplyConfigurationRequestContext)) ||
                        !options.Handlers.Exists(static descriptor => descriptor.ContextType == typeof(ApplyCryptographyRequestContext)))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0313));
                    }

                    registration.ConfigurationEndpoint = OpenIddictHelpers.CreateAbsoluteUri(
                        registration.Issuer,
                        registration.ConfigurationEndpoint ?? new Uri(".well-known/openid-configuration", UriKind.Relative));

                    registration.ConfigurationManager = new ConfigurationManager<OpenIddictConfiguration>(
                        registration.ConfigurationEndpoint.AbsoluteUri, new OpenIddictClientRetriever(_service, registration))
                    {
                        AutomaticRefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultAutomaticRefreshInterval,
                        RefreshInterval = ConfigurationManager<OpenIddictConfiguration>.DefaultRefreshInterval
                    };
                }
            }
        }

        // Implicitly add the redirect_uri attached to the client registrations
        // to the list of redirection endpoints URIs if they haven't been added.
        options.RedirectionEndpointUris.AddRange(options.Registrations
            .Where(registration => registration.RedirectUri is not null)
            .Select(registration => registration.RedirectUri!)
            .Where(uri => !options.RedirectionEndpointUris.Contains(uri))
            .Distinct()
            .ToList());

        // Implicitly add the post_logout_redirect_uri attached to the client registrations
        // to the list of post-logout redirection endpoints URIs if they haven't been added.
        options.PostLogoutRedirectionEndpointUris.AddRange(options.Registrations
            .Where(registration => registration.PostLogoutRedirectUri is not null)
            .Select(registration => registration.PostLogoutRedirectUri!)
            .Where(uri => !options.PostLogoutRedirectionEndpointUris.Contains(uri))
            .Distinct()
            .ToList());

        // Ensure at least one flow has been enabled.
        if (options.GrantTypes.Count is 0 && options.ResponseTypes.Count is 0)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var uris = options.RedirectionEndpointUris.Distinct()
            .Concat(options.PostLogoutRedirectionEndpointUris.Distinct())
            .ToList();

        // Ensure endpoint URIs are unique across endpoints.
        if (uris.Count != uris.Distinct().Count())
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0285));
        }

        // Ensure the redirection endpoint has been enabled when the authorization code or implicit grants are supported.
        if (options.RedirectionEndpointUris.Count is 0 && (options.GrantTypes.Contains(GrantTypes.AuthorizationCode) ||
                                                           options.GrantTypes.Contains(GrantTypes.Implicit)))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0356));
        }

        // Ensure the grant types/response types configuration is consistent.
        foreach (var type in options.ResponseTypes)
        {
            var types = type.Split(Separators.Space, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (types.Contains(ResponseTypes.Code) && !options.GrantTypes.Contains(GrantTypes.AuthorizationCode))
            {
                throw new InvalidOperationException(SR.FormatID0281(ResponseTypes.Code));
            }

            if (types.Contains(ResponseTypes.IdToken) && !options.GrantTypes.Contains(GrantTypes.Implicit))
            {
                throw new InvalidOperationException(SR.FormatID0282(ResponseTypes.IdToken));
            }

            if (types.Contains(ResponseTypes.Token) && !options.GrantTypes.Contains(GrantTypes.Implicit))
            {
                throw new InvalidOperationException(SR.FormatID0282(ResponseTypes.Token));
            }
        }

        // When the redirection or post-logout redirection endpoint has been enabled, ensure signing
        // and encryption credentials have been provided as they are required to protect state tokens.
        if (options.RedirectionEndpointUris.Count is not 0 || options.PostLogoutRedirectionEndpointUris.Count is not 0)
        {
            if (options.EncryptionCredentials.Count is 0)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0357));
            }

            if (options.SigningCredentials.Count is 0)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0358));
            }
        }

        // Ensure registration identifiers are not used in multiple client registrations.
        //
        // Note: a string comparer ignoring casing is deliberately used to prevent two
        // registrations using the same identifier with a different casing from being added.
        if (options.Registrations.Count != options.Registrations.Select(registration => registration.RegistrationId)
                                                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                                                .Count())
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0347));
        }

        // Sort the handlers collection using the order associated with each handler.
        options.Handlers.Sort((left, right) => left.Order.CompareTo(right.Order));

        // Sort the encryption and signing credentials.
        options.EncryptionCredentials.Sort((left, right) => Compare(left.Key, right.Key));
        options.SigningCredentials.Sort((left, right) => Compare(left.Key, right.Key));

        // Generate a key identifier for the encryption/signing keys that don't already have one.
        foreach (var key in options.EncryptionCredentials.Select(credentials => credentials.Key)
            .Concat(options.SigningCredentials.Select(credentials => credentials.Key))
            .Where(key => string.IsNullOrEmpty(key.KeyId)))
        {
            key.KeyId = GetKeyIdentifier(key);
        }

        // Attach the signing credentials to the token validation parameters.
        options.TokenValidationParameters.IssuerSigningKeys =
            from credentials in options.SigningCredentials
            select credentials.Key;

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

        static string? GetKeyIdentifier(SecurityKey key)
        {
            // When no key identifier can be retrieved from the security keys, a value is automatically
            // inferred from the hexadecimal representation of the certificate thumbprint (SHA-1)
            // when the key is bound to a X.509 certificate or from the public part of the signing key.

            if (key is X509SecurityKey x509SecurityKey)
            {
                return x509SecurityKey.Certificate.Thumbprint;
            }

            if (key is RsaSecurityKey rsaSecurityKey)
            {
                // Note: if the RSA parameters are not attached to the signing key,
                // extract them by calling ExportParameters on the RSA instance.
                var parameters = rsaSecurityKey.Parameters;
                if (parameters.Modulus is null)
                {
                    parameters = rsaSecurityKey.Rsa.ExportParameters(includePrivateParameters: false);

                    Debug.Assert(parameters.Modulus is not null, SR.GetResourceString(SR.ID4003));
                }

                // Only use the 40 first chars of the base64url-encoded modulus.
                var identifier = Base64UrlEncoder.Encode(parameters.Modulus);
                return identifier[..Math.Min(identifier.Length, 40)].ToUpperInvariant();
            }

#if SUPPORTS_ECDSA
            if (key is ECDsaSecurityKey ecsdaSecurityKey)
            {
                // Extract the ECDSA parameters from the signing credentials.
                var parameters = ecsdaSecurityKey.ECDsa.ExportParameters(includePrivateParameters: false);

                Debug.Assert(parameters.Q.X is not null, SR.GetResourceString(SR.ID4004));

                // Only use the 40 first chars of the base64url-encoded X coordinate.
                var identifier = Base64UrlEncoder.Encode(parameters.Q.X);
                return identifier[..Math.Min(identifier.Length, 40)].ToUpperInvariant();
            }
#endif

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void TransformBlock(HashAlgorithm algorithm, string input)
        {
            var buffer = Encoding.UTF8.GetBytes(input);
            algorithm.TransformBlock(buffer, 0, buffer.Length, outputBuffer: null, outputOffset: 0);
        }
    }
}
