namespace Be.Vlaanderen.Basisregisters.AspNetCore.Swagger.ReDoc
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc.ApiExplorer;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using NSwag;
    using NSwag.CodeGeneration;
    using NSwag.CodeGeneration.CSharp;
    using NSwag.CodeGeneration.TypeScript;

    public class SwaggerDocumentationOptions
    {
        /// <summary>
        /// Defines the behavior of a provider that discovers and describes API version information within an application.
        /// </summary>
        public IApiVersionDescriptionProvider ApiVersionDescriptionProvider { get; set; }

        /// <summary>
        /// Sets a title for the ReDoc page.
        /// </summary>
        public Func<string, string> DocumentTitleFunc { get; set; }

        /// <summary>
        /// Sets a header title for the ReDoc page.
        /// </summary>
        public Func<string, string> HeaderTitleFunc { get; set; }

        /// <summary>
        /// Sets a header link for the ReDoc page.
        /// </summary>
        public Func<string, string> HeaderLinkFunc { get; set; }

        /// <summary>
        /// Sets a version for the footer of the ReDoc page.
        /// </summary>
        public string FooterVersion { get; set; }

        public CSharpClientOptions CSharpClient { get; } = new CSharpClientOptions();

        public class CSharpClientOptions
        {
            public string ClassName { get; set; }
            public string Namespace { get; set; }
        }

        public TypeScriptClientOptions TypeScriptClient { get; } = new TypeScriptClientOptions();

        public class TypeScriptClientOptions
        {
            public string ClassName { get; set; }
        }
    }

    public static class UseSwaggerDocumentationExtensions
    {
        public static IApplicationBuilder UseSwaggerDocumentation(
            this IApplicationBuilder app,
            SwaggerDocumentationOptions options)
        {
            if (options.ApiVersionDescriptionProvider == null)
                throw new ArgumentNullException(nameof(options.ApiVersionDescriptionProvider));

            if (options.DocumentTitleFunc == null)
                throw new ArgumentNullException(nameof(options.DocumentTitleFunc));

            if (options.HeaderTitleFunc == null)
                options.HeaderTitleFunc = options.DocumentTitleFunc;

            if (options.HeaderLinkFunc == null)
                options.HeaderLinkFunc = x => "/";

            if (string.IsNullOrWhiteSpace(options.FooterVersion))
                options.FooterVersion = string.Empty;

            if (string.IsNullOrWhiteSpace(options.CSharpClient.ClassName))
                throw new ArgumentNullException(nameof(options.CSharpClient.ClassName));

            if (string.IsNullOrWhiteSpace(options.CSharpClient.Namespace))
                throw new ArgumentNullException(nameof(options.CSharpClient.Namespace));

            if (string.IsNullOrWhiteSpace(options.TypeScriptClient.ClassName))
                throw new ArgumentNullException(nameof(options.TypeScriptClient.ClassName));

            app
                .MapDocs(options)
                .MapClient(options, "csharp", GenerateCSharpCode)
                .MapClient(options, "jquery", GeneratejQueryCode)
                .MapClient(options, "angular", GenerateAngularCode)
                .MapClient(options, "angularjs", GenerateAngularJsCode);

            return app;
        }

        private static IApplicationBuilder MapDocs(this IApplicationBuilder app, SwaggerDocumentationOptions options)
        {
            app.Map(new PathString("/docs"), apiDocs =>
            {
                apiDocs.UseSwagger(x =>
                {
                    x.RouteTemplate = "{documentName}/docs.json";
                    x.PreSerializeFilters.Add((doc, _) => doc.BasePath = "/");
                });

                var apiVersions = options
                    .ApiVersionDescriptionProvider
                    .ApiVersionDescriptions
                    .Select(x => x.GroupName)
                    .OrderBy(x => x)
                    .ToList();

                var stringLocalizer = app.ApplicationServices.GetRequiredService<IStringLocalizer<Localization>>();

                foreach (var description in apiVersions)
                {
                    apiDocs.UseReDoc(x =>
                    {
                        x.DocumentTitle = stringLocalizer[options.DocumentTitleFunc(description)];
                        x.HeaderTitle = stringLocalizer[options.HeaderTitleFunc(description)];
                        x.HeaderLink = stringLocalizer[options.HeaderLinkFunc(description)];
                        x.FooterVersion = options.FooterVersion;
                        x.SpecUrl = $"/docs/{description}/docs.json";
                        x.RoutePrefix = $"{description}";
                    });
                }

                if (apiVersions.Count > 0)
                    apiDocs.UseReDoc(x =>
                    {
                        x.DocumentTitle = stringLocalizer[options.DocumentTitleFunc(apiVersions[0])];
                        x.HeaderTitle = stringLocalizer[options.HeaderTitleFunc(apiVersions[0])];
                        x.HeaderLink = stringLocalizer[options.HeaderLinkFunc(apiVersions[0])];
                        x.FooterVersion = options.FooterVersion;
                        x.SpecUrl = $"/docs/{apiVersions[0]}/docs.json";
                        x.RoutePrefix = string.Empty;
                    });
            });

            return app;
        }

        private static IApplicationBuilder MapClient(
            this IApplicationBuilder app,
            SwaggerDocumentationOptions options,
            string language,
            Func<SwaggerDocumentationOptions, string, OpenApiDocument, string> generateCode)
            => app.Map(new PathString($"/clients/{language}"), apiClients =>
            {
                var apiVersions = GetApiVersions(options);

                foreach (var description in apiVersions)
                {
                    apiClients.Map(new PathString($"/{description}"), apiClient => apiClient.Run(async context =>
                    {
                        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                        var document = await OpenApiDocument.FromUrlAsync($"{baseUrl}/docs/{description}/docs.json");

                        await context.Response.WriteAsync(generateCode(options, description, document));
                    }));
                }
            });

        private static string GenerateCSharpCode(
            SwaggerDocumentationOptions options,
            string description,
            OpenApiDocument document)
            => new CSharpClientGenerator(document, new CSharpClientGeneratorSettings
            {
                ClassName = $"{options.CSharpClient.ClassName}{description.FirstLetterToUpperCaseOrConvertNullToEmptyString()}",
                CSharpGeneratorSettings =
                {
                    Namespace = options.CSharpClient.Namespace
                }
            }).GenerateFile(ClientGeneratorOutputType.Full);

        private static string GenerateAngularCode(
            SwaggerDocumentationOptions options,
            string description,
            OpenApiDocument document)
            => new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings
            {
                ClassName =$"{options.TypeScriptClient.ClassName}{description.FirstLetterToUpperCaseOrConvertNullToEmptyString()}",
                Template = TypeScriptTemplate.Angular
            }).GenerateFile();

        private static string GenerateAngularJsCode(
            SwaggerDocumentationOptions options,
            string description,
            OpenApiDocument document)
            => new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings
            {
                ClassName = $"{options.TypeScriptClient.ClassName}{description.FirstLetterToUpperCaseOrConvertNullToEmptyString()}",
                Template = TypeScriptTemplate.AngularJS
            }).GenerateFile();

        private static string GeneratejQueryCode(
            SwaggerDocumentationOptions options,
            string description,
            OpenApiDocument document)
            => new TypeScriptClientGenerator(document, new TypeScriptClientGeneratorSettings
            {
                ClassName = $"{options.TypeScriptClient.ClassName}{description.FirstLetterToUpperCaseOrConvertNullToEmptyString()}",
                Template = TypeScriptTemplate.JQueryCallbacks
            }).GenerateFile();

        private static IEnumerable<string> GetApiVersions(SwaggerDocumentationOptions options)
            => options
                .ApiVersionDescriptionProvider
                .ApiVersionDescriptions
                .Select(x => x.GroupName)
                .OrderBy(x => x)
                .ToList();

        private static string FirstLetterToUpperCaseOrConvertNullToEmptyString(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }
}
