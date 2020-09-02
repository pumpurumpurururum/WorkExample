using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyCompany.Hotels.Core.Auth;
using MyCompany.Hotels.Core.Cache;
using Newtonsoft.Json;
using RestSharp;
using Newtonsoft.Json.Converters;
using MyCompany.Hotels.Core.Logging;
using MyCompany.Hotels.Core.Settings;
using MyCompany.Hotels.Core.Settings.Suppliers;
using MyCompany.Hotels.TestSupplier.Models.Requests;
using MyCompany.Platform.Shared.ObjectModel.Concrete.Common;
using MyCompany.Hotels.Core.ErrorMessages;
using MyCompany.Hotels.Core.Exceptions;
using MyCompany.Hotels.Core.Validation;
using MyCompany.Hotels.TestSupplier.Models;
using IO.Swagger.Model;
using MyCompany.Hotels.TestSupplier.Models.Responses;
using MyCompany.Hotels.Core.Exceptions;

namespace TestClient.Client
{

    public partial class ApiSupplierClient
    {
        private readonly IHotelLogger _hotelLogger;
        private readonly TestSupplierServiceSettings _settings;
        private readonly IAlternativeSupplierCredentialComponent _alternativeSupplierCredentialComponent;
        private readonly ICache _cache;

        private readonly int _timeOut = 100000;
        private readonly string _userAgent = "1.0.0/csharp";

        private string SupplierName() => "TestSupplier V2";

        private readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            Converters = new JsonConverter[] { new StringEnumConverter() }
        };

        /// <summary>
        /// Allows for extending request processing for <see cref="ApiClient"/> generated code.
        /// </summary>
        /// <param name="request">The RestSharp request object</param>
        partial void InterceptRequest(IRestRequest request);

        /// <summary>
        /// Allows for extending response processing for <see cref="ApiClient"/> generated code.
        /// </summary>
        /// <param name="request">The RestSharp request object</param>
        /// <param name="response">The RestSharp response object</param>
        partial void InterceptResponse(IRestRequest request, IRestResponse response);


        /// <summary>
        /// Initializes a new instance of the <see cref="ApiClient" /> class
        /// with default base path (https://localhost).
        /// </summary>
        /// <param name="config">An instance of Configuration.</param>
        public ApiClient(IHotelLogger hotelLogger, IAlternativeSupplierCredentialComponent alternativeSupplierCredentialComponent,
            TestSupplierServiceSettings settings, ICache cache)
        {
            _hotelLogger = hotelLogger;
            _settings = settings;
            _alternativeSupplierCredentialComponent = alternativeSupplierCredentialComponent;
            _cache = cache;
            RestClient = new RestClient(settings.Url);
        }

        /// <summary>
        /// Gets or sets the RestClient.
        /// </summary>
        /// <value>An instance of the RestClient</value>
        public RestClient RestClient { get; set; }

        // Creates and sets up a RestRequest prior to a call.
        private RestRequest PrepareRequest(
            String path, RestSharp.Method method, Dictionary<String, String> queryParams, Object postBody,
            Dictionary<String, String> headerParams, Dictionary<String, String> formParams,
            Dictionary<String, FileParameter> fileParams, Dictionary<String, String> pathParams,
            String contentType)
        {
            var request = new RestRequest(path, method);

            if(pathParams!=null)
            // add path parameter, if any
                foreach(var param in pathParams)
                    request.AddParameter(param.Key, param.Value, ParameterType.UrlSegment);

            if(headerParams!=null)
            // add header parameter, if any
                foreach(var param in headerParams)
                    request.AddHeader(param.Key, param.Value);

            if(queryParams!=null)
            // add query parameter, if any
                foreach(var param in queryParams)
                    request.AddQueryParameter(param.Key, param.Value);

            if(formParams!=null)
            // add form parameter, if any
                foreach(var param in formParams)
                    request.AddParameter(param.Key, param.Value);

            if(fileParams!=null)
            // add file parameter, if any
                foreach(var param in fileParams)
                {
                    request.AddFile(param.Value.Name, param.Value.Writer, param.Value.FileName, param.Value.ContentType);
                }

            if (postBody != null) // http body (model or byte[]) parameter
            {
                request.AddParameter(contentType, postBody, ParameterType.RequestBody);
            }

            return request;
        }

        
        public async Task<TResponse> CallApiPostAsync<TResponse, TRequest>(string path, TRequest req, BaseRequest brequest)
        {
            var postBody = Serialize(req);

            var locale = GetLanguageParameter(brequest.Language);

            var headerParams = new Dictionary<string, string>{{ "Accept", "application/json" }};

            var pathParams = new Dictionary<string, string>();
            if (locale != null)
                pathParams.Add("_locale", ParameterToString(locale));

            async Task<IRestResponse> Execute(bool p)
            {
                AuthPrepareRequest(brequest, headerParams, p);
                var request = PrepareRequest(path, Method.POST, null, postBody, headerParams, null, null, pathParams, "application/json");

                InterceptRequest(request);
                var response = await RestClient.ExecuteTaskAsync(request);
                InterceptResponse(request, response);
                return response;
            }

            var localVarResponse = await Execute(false);

            int localVarStatusCode = (int)localVarResponse.StatusCode;

            if (localVarStatusCode == 401)
                localVarResponse = await Execute(true);


            Guard.SupplierException(() => string.IsNullOrEmpty(localVarResponse.Content), "Поставщик вернул пустой ответ", ExceptionType.Error);

            try
            {
                return (TResponse) Deserialize(localVarResponse, typeof(TResponse));
            }
            catch (Exception e)
            {
                Guard.InternalException(() => true, "Ошибка десериализации ответа от TestSupplier", ExceptionType.Error);

            }

            return default(TResponse);
        }


        public async Task<TResponse> CallApiGetAsync<TResponse>(string path, BaseRequest brequest, Dictionary<string, string> pathParams, Dictionary<string, string> queryParams)
        {
            //var postBody = Serialize(req);

            var locale = GetLanguageParameter(brequest.Language);

            var headerParams = new Dictionary<string, string> { { "Accept", "application/json" } };

            if (locale != null)
                pathParams.Add("_locale", ParameterToString(locale));

            async Task<IRestResponse> Execute(bool p)
            {
                AuthPrepareRequest(brequest, headerParams, p);
                var request = PrepareRequest(path, Method.GET, queryParams, null, headerParams, null, null, pathParams, "application/json");

                InterceptRequest(request);
                var response = await RestClient.ExecuteTaskAsync(request);
                InterceptResponse(request, response);
                return response;
            }

            var localVarResponse = await Execute(false);

            int localVarStatusCode = (int)localVarResponse.StatusCode;

            if (localVarStatusCode == 401)
                localVarResponse = await Execute(true);

            string errorDescription;
            try
            {
                var errorResponse = (ErrorResponse)Deserialize(localVarResponse, typeof(ErrorResponse));
                errorDescription = errorResponse.Description;
            }
            catch
            {
                errorDescription = string.Empty;
            }

            if (!string.IsNullOrEmpty(errorDescription))
                throw new SupplierException(errorDescription, ExceptionType.Error);


            Guard.SupplierException(() => string.IsNullOrEmpty(localVarResponse.Content), "Поставщик вернул пустой ответ", ExceptionType.Error);
            try
            {
                return (TResponse) Deserialize(localVarResponse, typeof(TResponse));
            }
            catch (Exception e)
            {
                Guard.InternalException(() => true, "Ошибка десериализации ответа от TestSupplier", ExceptionType.Error);

            }

            return default(TResponse);
        }


        private string GetAuthTokenInternal(string login, string password, string locale)
        {
            if (string.IsNullOrEmpty(login))
                throw new ApiException(400, $"Missing required parameter 'login' when calling {nameof(GetAuthTokenInternal)}");

            if (string.IsNullOrEmpty(password))
                throw new ApiException(400, $"Missing required parameter 'password' when calling {nameof(GetAuthTokenInternal)}");

            if (string.IsNullOrEmpty(locale))
                throw new ApiException(400, $"Missing required parameter 'locale' when calling {nameof(GetAuthTokenInternal)}");

            

            var localVarPath = "/api/v1/{_locale}/gateway/login";
            var localVarPathParams = new Dictionary<string, string>();
            var localVarHeaderParams = new Dictionary<string, string>{{ "Accept","application/json" }};
 
            if (locale != null) localVarPathParams.Add("_locale", ParameterToString(locale));
            
            var loginRequest = new LoginRequest(login, password);
            object localVarPostBody = null;

            if (loginRequest != null && loginRequest.GetType() != typeof(byte[]))
            {
                localVarPostBody = Serialize(loginRequest);
            }
            else
            {
                localVarPostBody = loginRequest;
            }

 
            var request = PrepareRequest(
                localVarPath, Method.POST, null, localVarPostBody, localVarHeaderParams, null, null,
                localVarPathParams, "application/json");


            RestClient.Timeout = _timeOut;

            RestClient.UserAgent = _userAgent;

            InterceptRequest(request);

            var response = RestClient.Execute(request);

            Guard.SupplierException(() => string.IsNullOrEmpty(response.Content), "Поставщик вернул пустой ответ", ExceptionType.Error);

            InterceptResponse(request, response);

            try
            {
                var tokenObject = JsonConvert.DeserializeObject<AccessTokenResponseModel>(response.Content);

                return tokenObject.AccessToken;
            }
            catch (Exception e)
            {
                Guard.InternalException(()=> true, "Ошибка десериализации ответа от TestSupplier", ExceptionType.Error);
                
            }

            return null;
        }



        /// <summary>
        /// Create FileParameter based on Stream.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="stream">Input stream.</param>
        /// <returns>FileParameter.</returns>
        public FileParameter ParameterToFile(string name, Stream stream)
        {
            if (stream is FileStream)
                return FileParameter.Create(name, ReadAsBytes(stream), Path.GetFileName(((FileStream)stream).Name));
            else
                return FileParameter.Create(name, ReadAsBytes(stream), "no_file_name_provided");
        }

        /// <summary>
        /// If parameter is DateTime, output in a formatted string (default ISO 8601), customizable with Configuration.DateTime.
        /// If parameter is a list, join the list with ",".
        /// Otherwise just return the string.
        /// </summary>
        /// <param name="obj">The parameter (header, path, query, form).</param>
        /// <returns>Formatted string.</returns>
        public string ParameterToString(object obj)
        {
            if (obj is DateTime)
                // Return a formatted date string - Can be customized with Configuration.DateTimeFormat
                // Defaults to an ISO 8601, using the known as a Round-trip date/time pattern ("o")
                // https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#Anchor_8
                // For example: 2009-06-15T13:45:30.0000000
                return ((DateTime)obj).ToString (DateTimeFormat);
            else if (obj is DateTimeOffset)
                // Return a formatted date string - Can be customized with Configuration.DateTimeFormat
                // Defaults to an ISO 8601, using the known as a Round-trip date/time pattern ("o")
                // https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#Anchor_8
                // For example: 2009-06-15T13:45:30.0000000
                return ((DateTimeOffset)obj).ToString (DateTimeFormat);
            else if (obj is IList)
            {
                var flattenedString = new StringBuilder();
                foreach (var param in (IList)obj)
                {
                    if (flattenedString.Length > 0)
                        flattenedString.Append(",");
                    flattenedString.Append(param);
                }
                return flattenedString.ToString();
            }
            else
                return Convert.ToString (obj);
        }

        /// <summary>
        /// Identifier for ISO 8601 DateTime Format
        /// </summary>
        /// <remarks>See https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#Anchor_8 for more information.</remarks>
        // ReSharper disable once InconsistentNaming
        public const string ISO8601_DATETIME_FORMAT = "o";
        private string _dateTimeFormat = ISO8601_DATETIME_FORMAT;
        /// <summary>
        /// Gets or sets the the date time format used when serializing in the ApiClient
        /// By default, it's set to ISO 8601 - "o", for others see:
        /// https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx
        /// and https://msdn.microsoft.com/en-us/library/8kb3ddd4(v=vs.110).aspx
        /// No validation is done to ensure that the string you're providing is valid
        /// </summary>
        /// <value>The DateTimeFormat string</value>
        public virtual string DateTimeFormat
        {
            get { return _dateTimeFormat; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    // Never allow a blank or null string, go back to the default
                    _dateTimeFormat = ISO8601_DATETIME_FORMAT;
                    return;
                }

                // Caution, no validation when you choose date time format other than ISO 8601
                // Take a look at the above links
                _dateTimeFormat = value;
            }
        }

        /// <summary>
        /// Deserialize the JSON string into a proper object.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <param name="type">Object type.</param>
        /// <returns>Object representation of the JSON string.</returns>
        public object Deserialize(IRestResponse response, Type type)
        {
            IList<Parameter> headers = response.Headers;
            if (type == typeof(byte[])) // return byte array
            {
                return response.RawBytes;
            }

            // TODO: ? if (type.IsAssignableFrom(typeof(Stream)))
            //if (type == typeof(Stream))
            //{
            //    if (headers != null)
            //    {
            //        var filePath = String.IsNullOrEmpty(Configuration.TempFolderPath)
            //            ? Path.GetTempPath()
            //            : Configuration.TempFolderPath;
            //        var regex = new Regex(@"Content-Disposition=.*filename=['""]?([^'""\s]+)['""]?$");
            //        foreach (var header in headers)
            //        {
            //            var match = regex.Match(header.ToString());
            //            if (match.Success)
            //            {
            //                string fileName = filePath + SanitizeFilename(match.Groups[1].Value.Replace("\"", "").Replace("'", ""));
            //                File.WriteAllBytes(fileName, response.RawBytes);
            //                return new FileStream(fileName, FileMode.Open);
            //            }
            //        }
            //    }
            //    var stream = new MemoryStream(response.RawBytes);
            //    return stream;
            //}

            if (type.Name.StartsWith("System.Nullable`1[[System.DateTime")) // return a datetime object
            {
                return DateTime.Parse(response.Content,  null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            if (type == typeof(String) || type.Name.StartsWith("System.Nullable")) // return primitive type
            {
                return ConvertType(response.Content, type);
            }

            // at this point, it must be a model (json)
            try
            {
                return JsonConvert.DeserializeObject(response.Content, type, serializerSettings);
            }
            catch (Exception e)
            {
                throw new ApiException(500, e.Message);
            }
        }

        /// <summary>
        /// Serialize an input (model) into JSON string
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <returns>JSON string.</returns>
        public String Serialize(object obj)
        {
            try
            {
                return obj != null ? JsonConvert.SerializeObject(obj) : null;
            }
            catch (Exception e)
            {
                throw new ApiException(500, e.Message);
            }
        }

        /// <summary>
        ///Check if the given MIME is a JSON MIME.
        ///JSON MIME examples:
        ///    application/json
        ///    application/json; charset=UTF8
        ///    APPLICATION/JSON
        ///    application/vnd.company+json
        /// </summary>
        /// <param name="mime">MIME</param>
        /// <returns>Returns True if MIME type is json.</returns>
        public bool IsJsonMime(String mime)
        {
            var jsonRegex = new Regex("(?i)^(application/json|[^;/ \t]+/[^;/ \t]+[+]json)[ \t]*(;.*)?$");
            return mime != null && (jsonRegex.IsMatch(mime) || mime.Equals("application/json-patch+json"));
        }

        /// <summary>
        /// Select the Content-Type header's value from the given content-type array:
        /// if JSON type exists in the given array, use it;
        /// otherwise use the first one defined in 'consumes'
        /// </summary>
        /// <param name="contentTypes">The Content-Type array to select from.</param>
        /// <returns>The Content-Type header to use.</returns>
        public String SelectHeaderContentType(String[] contentTypes)
        {
            if (contentTypes.Length == 0)
                return "application/json";

            foreach (var contentType in contentTypes)
            {
                if (IsJsonMime(contentType.ToLower()))
                    return contentType;
            }

            return contentTypes[0]; // use the first content type specified in 'consumes'
        }

        /// <summary>
        /// Select the Accept header's value from the given accepts array:
        /// if JSON exists in the given array, use it;
        /// otherwise use all of them (joining into a string)
        /// </summary>
        /// <param name="accepts">The accepts array to select from.</param>
        /// <returns>The Accept header to use.</returns>
        public String SelectHeaderAccept(String[] accepts)
        {
            if (accepts.Length == 0)
                return null;

            if (accepts.Contains("application/json", StringComparer.OrdinalIgnoreCase))
                return "application/json";

            return String.Join(",", accepts);
        }

        /// <summary>
        /// Encode string in base64 format.
        /// </summary>
        /// <param name="text">String to be encoded.</param>
        /// <returns>Encoded string.</returns>
        public static string Base64Encode(string text)
        {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Dynamically cast the object into target type.
        /// </summary>
        /// <param name="fromObject">Object to be casted</param>
        /// <param name="toObject">Target type</param>
        /// <returns>Casted object</returns>
        public static dynamic ConvertType(dynamic fromObject, Type toObject)
        {
            return Convert.ChangeType(fromObject, toObject);
        }

        /// <summary>
        /// Convert stream to byte array
        /// </summary>
        /// <param name="inputStream">Input stream to be converted</param>
        /// <returns>Byte array</returns>
        public static byte[] ReadAsBytes(Stream inputStream)
        {
            byte[] buf = new byte[16*1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int count;
                while ((count = inputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, count);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// URL encode a string
        /// Credit/Ref: https://github.com/restsharp/RestSharp/blob/master/RestSharp/Extensions/StringExtensions.cs#L50
        /// </summary>
        /// <param name="input">String to be URL encoded</param>
        /// <returns>Byte array</returns>
        public static string UrlEncode(string input)
        {
            const int maxLength = 32766;

            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            if (input.Length <= maxLength)
            {
                return Uri.EscapeDataString(input);
            }

            StringBuilder sb = new StringBuilder(input.Length * 2);
            int index = 0;

            while (index < input.Length)
            {
                int length = Math.Min(input.Length - index, maxLength);
                string subString = input.Substring(index, length);

                sb.Append(Uri.EscapeDataString(subString));
                index += subString.Length;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Sanitize filename by removing the path
        /// </summary>
        /// <param name="filename">Filename</param>
        /// <returns>Filename</returns>
        public static string SanitizeFilename(string filename)
        {
            Match match = Regex.Match(filename, @".*[/\\](.*)$");

            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                return filename;
            }
        }

        /// <summary>
        /// Convert params to key/value pairs. 
        /// Use collectionFormat to properly format lists and collections.
        /// </summary>
        /// <param name="name">Key name.</param>
        /// <param name="value">Value object.</param>
        /// <returns>A list of KeyValuePairs</returns>
        public IEnumerable<KeyValuePair<string, string>> ParameterToKeyValuePairs(string collectionFormat, string name, object value)
        {
            var parameters = new List<KeyValuePair<string, string>>();

            if (IsCollection(value) && collectionFormat == "multi")
            {
                var valueCollection = value as IEnumerable;
                parameters.AddRange(from object item in valueCollection select new KeyValuePair<string, string>(name, ParameterToString(item)));
            }
            else
            {
                parameters.Add(new KeyValuePair<string, string>(name, ParameterToString(value)));
            }

            return parameters;
        }

        /// <summary>
        /// Check if generic object is a collection.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>True if object is a collection type</returns>
        private static bool IsCollection(object value)
        {
            return value is IList || value is ICollection;
        }

        /***********************************************************************/

        private string GetLanguageParameter(Language language)
        {
            return language == Language.English ? "en" : "ru";
        }

        internal void AuthPrepareRequest(BaseRequest request, Dictionary<string, string> headers, bool updateToken = false)
        {
            var authData = GetAuthParams(request.EmployeeId, request.ClientId);

            if (string.IsNullOrWhiteSpace(authData.username) || string.IsNullOrWhiteSpace(authData.password))
            {
                throw new ArgumentException(string.Format(Errors.CredentialsNotProvidedForSupplier, SupplierName()));
            }

            var language = GetLanguageParameter(request.Language);
            var token = GetAuthTokenCachedInternal(authData.username, authData.password, language, request.EmployeeId, request.ClientId, updateToken);

            if (!String.IsNullOrEmpty(token))
            {
                headers["Authorization"] = $"Bearer {token}";
            }
            else
            {
                throw new ApiException(401, "Failed to get token");
            }
        }

        private string GetAuthTokenCachedInternal(string login, string password, string locale, int employeeId, int? clientId, bool updateToken)
        {
            var baseKey = new List<string>{"Service.Hotels:TestSupplier:AuthToken", locale, employeeId.ToString()};
            if(clientId.HasValue)
                baseKey.Add(clientId.ToString());
            var key = string.Join("_", baseKey);

            string token = string.Empty;

            if (updateToken || !_cache.Get<string>(key, out token))
            {
                var period = new TimeSpan(23, 0, 0);

                token = GetAuthTokenInternal(login, password, locale);
                _cache.Add<string>(key, token, period);
            }
            return token;
        }

        private (string username, string password) GetAuthParams(int employeeId, int? clientId = null)
        {
            if (clientId.HasValue)
            {
                var aspSupplierCredential = _alternativeSupplierCredentialComponent.GetASPCredential(Constants.TestSupplier_GUID, clientId.Value, employeeId);

                if (aspSupplierCredential != null)
                    return (username: aspSupplierCredential.Login, password: aspSupplierCredential.Password);
            }

            return (username: _settings.Username, password: _settings.Password);
        }
    }
}
