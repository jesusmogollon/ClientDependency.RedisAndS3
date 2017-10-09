# ClientDependency.RedisAndS3
This project has a File Map Store for Redis, the files index files are stored in json format in the provided Redis Cache server.
Requires StackExchange.Redis
Also contains an additional S3 composite processor to storage the final files in a S3 bucket.
Requires the amazon sdk for s3

The code files are in the ClientDependencyProviders folder, these two files and the custom configuration for ClientDependency is all you need in your project.

The custom entry in the web.config should look like this:
```xml
  <clientDependency version="10">
    <compositeFiles>
      <fileProcessingProviders>
        <!-- 
        A file processor provider for final files aws S3 bucket.
        Needs Nuget libraries: AWSSDK.S3
        You will need read/write access to the bucket with the provided credentials
        -->
        <add name="CompositeFileProcessor"
             type="ClientDependency.Core.CompositeFiles.Providers.CompositeS3FileProcessorProvider, ClientDependency.RedisAndS3"
             bucketRegion="YOUR REGION HERE LIKE:us-west-2"
             bucketName="YOUR BUCKET HERE"
             accessKey="YOUR ACCESS KEY HERE"
             secretKey="YOUR SECRET KEY HERE" />
      </fileProcessingProviders>
      <!-- 
      A file map provider store for map files in Redis Cache, format is json.
      Needs Nuget libraries: Newtonsoft.Json and StackExchange.Redis
      -->
      <fileMapProviders>
        <add name="XmlFileMap"
             type="ClientDependency.Core.CompositeFiles.Providers.RedisXmlFileMapper, ClientDependency.RedisAndS3"
             mapPath="~/ClientDependency/Cache"
             dbNum="2"
             redisCacheConnection="localhost"/>
      </fileMapProviders>
    </compositeFiles>
  </clientDependency>
  ```
  Replace the 'ClientDependency.RedisAndS3' with your assembly name.
