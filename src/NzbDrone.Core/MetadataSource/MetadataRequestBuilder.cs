using System;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataRequestBuilder
    {
        IHttpRequestBuilderFactory GetRequestBuilder();
    }

    public class MetadataRequestBuilder : IMetadataRequestBuilder
    {
        private static readonly string MetadataUrl =
            Environment.GetEnvironmentVariable("READARR_METADATA_URL")
            ?? "http://localhost:28202/{route}";

        public IHttpRequestBuilderFactory GetRequestBuilder()
        {
            return new HttpRequestBuilder(MetadataUrl).KeepAlive().CreateFactory();
        }
    }
}
