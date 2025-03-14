// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.Net.Http.Headers
{
    public class MediaTypeHeaderValue : ICloneable
    {
        private const string charSet = "charset";

        private UnvalidatedObjectCollection<NameValueHeaderValue>? _parameters;
        private string? _mediaType;

        public string? CharSet
        {
            get
            {
                NameValueHeaderValue? charSetParameter = NameValueHeaderValue.Find(_parameters, charSet);
                if (charSetParameter != null)
                {
                    return charSetParameter.Value;
                }
                return null;
            }
            set
            {
                // We don't prevent a user from setting whitespace-only charsets. Like we can't prevent a user from
                // setting a non-existing charset.
                NameValueHeaderValue? charSetParameter = NameValueHeaderValue.Find(_parameters, charSet);
                if (string.IsNullOrEmpty(value))
                {
                    // Remove charset parameter
                    if (charSetParameter != null)
                    {
                        _parameters!.Remove(charSetParameter);
                    }
                }
                else
                {
                    if (charSetParameter != null)
                    {
                        charSetParameter.Value = value;
                    }
                    else
                    {
                        Parameters.Add(new NameValueHeaderValue(charSet, value));
                    }
                }
            }
        }

        public ICollection<NameValueHeaderValue> Parameters => _parameters ??= new UnvalidatedObjectCollection<NameValueHeaderValue>();

        [DisallowNull]
        public string? MediaType
        {
            get { return _mediaType; }
            set
            {
                CheckMediaTypeFormat(value, nameof(value));
                _mediaType = value;
            }
        }

        internal MediaTypeHeaderValue()
        {
            // Used by the parser to create a new instance of this type.
        }

        protected MediaTypeHeaderValue(MediaTypeHeaderValue source)
        {
            Debug.Assert(source != null);

            _mediaType = source._mediaType;
            _parameters = source._parameters.Clone();
        }

        public MediaTypeHeaderValue(string mediaType)
            : this(mediaType, charSet: null)
        {
        }

        public MediaTypeHeaderValue(string mediaType, string? charSet)
        {
            CheckMediaTypeFormat(mediaType, nameof(mediaType));
            _mediaType = mediaType;

            if (!string.IsNullOrEmpty(charSet))
            {
                CharSet = charSet;
            }
        }

        public override string ToString()
        {
            if (_parameters is null || _parameters.Count == 0)
            {
                return _mediaType ?? string.Empty;
            }

            var sb = StringBuilderCache.Acquire();
            sb.Append(_mediaType);
            NameValueHeaderValue.ToString(_parameters, ';', true, sb);
            return StringBuilderCache.GetStringAndRelease(sb);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            MediaTypeHeaderValue? other = obj as MediaTypeHeaderValue;

            if (other == null)
            {
                return false;
            }

            return string.Equals(_mediaType, other._mediaType, StringComparison.OrdinalIgnoreCase) &&
                HeaderUtilities.AreEqualCollections(_parameters, other._parameters);
        }

        public override int GetHashCode()
        {
            // The media-type string is case-insensitive.
            return StringComparer.OrdinalIgnoreCase.GetHashCode(_mediaType!) ^ NameValueHeaderValue.GetHashCode(_parameters);
        }

        public static MediaTypeHeaderValue Parse(string? input)
        {
            int index = 0;
            return (MediaTypeHeaderValue)MediaTypeHeaderParser.SingleValueParser.ParseValue(input, null, ref index);
        }

        public static bool TryParse([NotNullWhen(true)] string? input, [NotNullWhen(true)] out MediaTypeHeaderValue? parsedValue)
        {
            int index = 0;
            parsedValue = null;

            if (MediaTypeHeaderParser.SingleValueParser.TryParseValue(input, null, ref index, out object? output))
            {
                parsedValue = (MediaTypeHeaderValue)output!;
                return true;
            }
            return false;
        }

        internal static int GetMediaTypeLength(string? input, int startIndex,
            Func<MediaTypeHeaderValue> mediaTypeCreator, out MediaTypeHeaderValue? parsedValue)
        {
            Debug.Assert(mediaTypeCreator != null);
            Debug.Assert(startIndex >= 0);

            parsedValue = null;

            if (string.IsNullOrEmpty(input) || (startIndex >= input.Length))
            {
                return 0;
            }

            // Caller must remove leading whitespace. If not, we'll return 0.
            int mediaTypeLength = MediaTypeHeaderValue.GetMediaTypeExpressionLength(input, startIndex, out string? mediaType);

            if (mediaTypeLength == 0)
            {
                return 0;
            }

            int current = startIndex + mediaTypeLength;
            current += HttpRuleParser.GetWhitespaceLength(input, current);
            MediaTypeHeaderValue mediaTypeHeader;

            // If we're not done and we have a parameter delimiter, then we have a list of parameters.
            if ((current < input.Length) && (input[current] == ';'))
            {
                mediaTypeHeader = mediaTypeCreator();
                mediaTypeHeader._mediaType = mediaType;

                current++; // skip delimiter.
                int parameterLength = NameValueHeaderValue.GetNameValueListLength(input, current, ';',
                    (UnvalidatedObjectCollection<NameValueHeaderValue>)mediaTypeHeader.Parameters);

                if (parameterLength == 0)
                {
                    return 0;
                }

                parsedValue = mediaTypeHeader;
                return current + parameterLength - startIndex;
            }

            // We have a media type without parameters.
            mediaTypeHeader = mediaTypeCreator();
            mediaTypeHeader._mediaType = mediaType;
            parsedValue = mediaTypeHeader;
            return current - startIndex;
        }

        private static int GetMediaTypeExpressionLength(string input, int startIndex, out string? mediaType)
        {
            Debug.Assert((input != null) && (input.Length > 0) && (startIndex < input.Length));

            // This method just parses the "type/subtype" string, it does not parse parameters.
            mediaType = null;

            // Parse the type, i.e. <type> in media type string "<type>/<subtype>; param1=value1; param2=value2"
            int typeLength = HttpRuleParser.GetTokenLength(input, startIndex);

            if (typeLength == 0)
            {
                return 0;
            }

            int current = startIndex + typeLength;
            current += HttpRuleParser.GetWhitespaceLength(input, current);

            // Parse the separator between type and subtype
            if ((current >= input.Length) || (input[current] != '/'))
            {
                return 0;
            }
            current++; // skip delimiter.
            current += HttpRuleParser.GetWhitespaceLength(input, current);

            // Parse the subtype, i.e. <subtype> in media type string "<type>/<subtype>; param1=value1; param2=value2"
            int subtypeLength = HttpRuleParser.GetTokenLength(input, current);

            if (subtypeLength == 0)
            {
                return 0;
            }

            // If there is no whitespace between <type> and <subtype> in <type>/<subtype> get the media type using
            // one Substring call. Otherwise get substrings for <type> and <subtype> and combine them.
            int mediaTypeLength = current + subtypeLength - startIndex;
            if (typeLength + subtypeLength + 1 == mediaTypeLength)
            {
                mediaType = input.Substring(startIndex, mediaTypeLength);
            }
            else
            {
                mediaType = string.Concat(input.AsSpan(startIndex, typeLength), "/", input.AsSpan(current, subtypeLength));
            }

            return mediaTypeLength;
        }

        private static void CheckMediaTypeFormat(string mediaType, string parameterName)
        {
            if (string.IsNullOrEmpty(mediaType))
            {
                throw new ArgumentException(SR.net_http_argument_empty_string, parameterName);
            }

            // When adding values using strongly typed objects, no leading/trailing LWS (whitespace) are allowed.
            // Also no LWS between type and subtype are allowed.
            int mediaTypeLength = GetMediaTypeExpressionLength(mediaType, 0, out string? tempMediaType);
            if ((mediaTypeLength == 0) || (tempMediaType!.Length != mediaType.Length))
            {
                throw new FormatException(SR.Format(System.Globalization.CultureInfo.InvariantCulture, SR.net_http_headers_invalid_value, mediaType));
            }
        }

        // Implement ICloneable explicitly to allow derived types to "override" the implementation.
        object ICloneable.Clone()
        {
            return new MediaTypeHeaderValue(this);
        }
    }
}
