﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;

namespace OpenIddict.Validation.SystemNetHttp;

public static partial class OpenIddictValidationSystemNetHttpHandlers
{
    public static class Discovery
    {
        public static ImmutableArray<OpenIddictValidationHandlerDescriptor> DefaultHandlers { get; } = [
            /*
             * Configuration request processing:
             */
            CreateHttpClient<PrepareConfigurationRequestContext>.Descriptor,
            PrepareGetHttpRequest<PrepareConfigurationRequestContext>.Descriptor,
            AttachHttpVersion<PrepareConfigurationRequestContext>.Descriptor,
            AttachJsonAcceptHeaders<PrepareConfigurationRequestContext>.Descriptor,
            AttachUserAgentHeader<PrepareConfigurationRequestContext>.Descriptor,
            AttachFromHeader<PrepareConfigurationRequestContext>.Descriptor,
            AttachHttpParameters<PrepareConfigurationRequestContext>.Descriptor,
            SendHttpRequest<ApplyConfigurationRequestContext>.Descriptor,
            DisposeHttpRequest<ApplyConfigurationRequestContext>.Descriptor,

            /*
             * Configuration response processing:
             */
            DecompressResponseContent<ExtractConfigurationResponseContext>.Descriptor,
            ExtractJsonHttpResponse<ExtractConfigurationResponseContext>.Descriptor,
            ExtractWwwAuthenticateHeader<ExtractConfigurationResponseContext>.Descriptor,
            ValidateHttpResponse<ExtractConfigurationResponseContext>.Descriptor,
            DisposeHttpResponse<ExtractConfigurationResponseContext>.Descriptor,

            /*
             * Cryptography request processing:
             */
            CreateHttpClient<PrepareCryptographyRequestContext>.Descriptor,
            PrepareGetHttpRequest<PrepareCryptographyRequestContext>.Descriptor,
            AttachHttpVersion<PrepareCryptographyRequestContext>.Descriptor,
            AttachJsonAcceptHeaders<PrepareCryptographyRequestContext>.Descriptor,
            AttachUserAgentHeader<PrepareCryptographyRequestContext>.Descriptor,
            AttachFromHeader<PrepareCryptographyRequestContext>.Descriptor,
            AttachHttpParameters<PrepareCryptographyRequestContext>.Descriptor,
            SendHttpRequest<ApplyCryptographyRequestContext>.Descriptor,
            DisposeHttpRequest<ApplyCryptographyRequestContext>.Descriptor,

            /*
             * Configuration response processing:
             */
            DecompressResponseContent<ExtractCryptographyResponseContext>.Descriptor,
            ExtractJsonHttpResponse<ExtractCryptographyResponseContext>.Descriptor,
            ExtractWwwAuthenticateHeader<ExtractCryptographyResponseContext>.Descriptor,
            ValidateHttpResponse<ExtractCryptographyResponseContext>.Descriptor,
            DisposeHttpResponse<ExtractCryptographyResponseContext>.Descriptor
        ];
    }
}
