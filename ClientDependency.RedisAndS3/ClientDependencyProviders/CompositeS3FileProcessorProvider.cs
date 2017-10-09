using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using ClientDependency.Core.Config;
using System.IO;
using System.Web;
using Amazon.S3;
using Amazon.S3.Transfer;

namespace ClientDependency.Core.CompositeFiles.Providers
{

    /// <summary>
    /// A provider for combining, minifying, compressing and saving composite scripts/css files
    /// </summary>
    public class CompositeS3FileProcessorProvider : BaseCompositeFileProcessingProvider
    {
        public const string DefaultName = "CompositeFileProcessor";

        public static string BucketName = null;
        public static string BucketRegion = null;
        public static string AccessKey = null;
        public static string SecretKey = null;
        public static string S3BasePath = null;

        public static bool HasS3Settings
        {
            get
            {
                return !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey) && !string.IsNullOrEmpty(BucketRegion);
            }
        }


        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(name, config);

            if (config == null)
                return;

            // bucket name
            if (!string.IsNullOrEmpty(config["bucketName"])) BucketName = config["bucketName"].ToString();
            else throw new ArgumentNullException("bucketName is empty in the CompositeFileProcessor entry for ClientDependency");

            // bucketRegion
            if (!string.IsNullOrEmpty(config["bucketRegion"])) BucketRegion = config["bucketRegion"].ToString();
            else throw new ArgumentNullException("bucketRegion is empty in the CompositeFileProcessor entry for ClientDependency");

            // accessKey
            if (!string.IsNullOrEmpty(config["accessKey"])) AccessKey = config["accessKey"].ToString();
            else throw new ArgumentNullException("accessKey is empty in the CompositeFileProcessor entry for ClientDependency");

            // secretKey
            if (!string.IsNullOrEmpty(config["secretKey"])) SecretKey = config["secretKey"].ToString();
            else throw new ArgumentNullException("secretKey is empty in the CompositeFileProcessor entry for ClientDependency");

            // S3 base path
            if (!string.IsNullOrEmpty(config["s3BasePath"]))
                S3BasePath = config["s3BasePath"].ToString();
            else
                S3BasePath = "ClientDependency/Files/";
        }

        /// <summary>
        /// Saves the file's bytes to disk with a hash of the byte array
        /// </summary>
        /// <param name="fileContents"></param>
        /// <param name="type"></param>
        /// <param name="server"></param>
        /// <returns>The new file path</returns>
        /// <remarks>
        /// the extension will be: .js for JavaScript and .css for CSS
        /// </remarks>
        public override FileInfo SaveCompositeFile(byte[] fileContents,
            ClientDependencyType type,
            HttpServerUtilityBase server)
        {
            //don't save the file if composite files are disabled.
            if (!PersistCompositeFiles)
                return null;

            var ext = type.ToString().ToLower().Replace("javascript", "js");
            var fileKey = Guid.NewGuid().ToString("N");
            var fName = string.Format("{0}-{1}.{2}", ClientDependencySettings.Instance.Version, fileKey, ext);
            var fi = new FileInfo(fName);
            string urlPath = string.Format("{0}/{1}", S3BasePath.TrimStart('/').TrimEnd('/'), fi.Name.Replace('-', '/'));

            try
            {
                using (var memFile = new MemoryStream(fileContents))
                {
                    memFile.Seek(0, SeekOrigin.Begin);
                    using (var client = new AmazonS3Client(AccessKey, SecretKey, Amazon.RegionEndpoint.GetBySystemName(BucketRegion)))
                    {
                        using (TransferUtility transf = new TransferUtility(client))
                        {
                            transf.Upload(memFile, BucketName, urlPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ClientDependencySettings.Instance.Logger.Error("Error writing ClientDependency file to s3 bucket " + fi.FullName, ex);
                throw new Exception("Error writing ClientDependency file to s3 bucket " + urlPath, ex);
            }

            return fi;
        }

        /// <summary>
        /// combines all files to a byte array
        /// </summary>
        /// <param name="filePaths"></param>
        /// <param name="context"></param>
        /// <param name="type"></param>
        /// <param name="fileDefs"></param>
        /// <returns></returns>
        public override byte[] CombineFiles(string[] filePaths, HttpContextBase context, ClientDependencyType type, out List<CompositeFileDefinition> fileDefs)
        {
            var ms = new MemoryStream(5000);
            var sw = new StreamWriter(ms, Encoding.UTF8);

            var fDefs = filePaths.Select(s => WritePathToStream(type, s, context, sw)).Where(def => def != null).ToList();

            sw.Flush();
            byte[] outputBytes = ms.ToArray();
            sw.Close();
            ms.Close();
            fileDefs = fDefs;
            return outputBytes;
        }

        /// <summary>
        /// Compresses the bytes if the browser supports it
        /// </summary>
        public override byte[] CompressBytes(CompressionType type, byte[] fileBytes)
        {
            return SimpleCompressor.CompressBytes(type, fileBytes);
        }

        /// <summary>
        /// Writes the output of an external request to the stream
        /// </summary>
        /// <param name="sw"></param>
        /// <param name="url"></param>
        /// <param name="type"></param>
        /// <param name="fileDefs"></param>
        /// <param name="http"></param>
        /// <returns></returns>
        [Obsolete("Use the equivalent method without the 'ref' parameters")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void WriteFileToStream(ref StreamWriter sw, string url, ClientDependencyType type, ref List<CompositeFileDefinition> fileDefs, HttpContextBase http)
        {
            var def = WriteFileToStream(sw, url, type, http);
            if (def != null)
            {
                fileDefs.Add(def);
            }
        }

        /// <summary>
        ///  Writes the output of a local file to the stream
        /// </summary>
        /// <param name="sw"></param>
        /// <param name="fi"></param>
        /// <param name="type"></param>
        /// <param name="origUrl"></param>
        /// <param name="fileDefs"></param>
        /// <param name="http"></param>
        [Obsolete("Use the equivalent method without the 'ref' parameters")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void WriteFileToStream(ref StreamWriter sw, FileInfo fi, ClientDependencyType type, string origUrl, ref List<CompositeFileDefinition> fileDefs, HttpContextBase http)
        {
            var def = WriteFileToStream(sw, fi, type, origUrl, http);
            if (def != null)
            {
                fileDefs.Add(def);
            }
        }

        public static byte[] ReadS3File(string file)
        {
            if (!HasS3Settings)
                return null;
            var fName = Path.GetFileName(file);
            var url = string.Format("{0}/{1}", S3BasePath.TrimStart('/').TrimEnd('/'), fName.Replace('-', '/'));
            try
            {
                using (var client = new AmazonS3Client(AccessKey, SecretKey, Amazon.RegionEndpoint.GetBySystemName(BucketRegion)))
                {
                    using (TransferUtility transf = new TransferUtility(client))
                    {
                        var stream = transf.OpenStream(BucketName, url);
                        using (var memFile = new MemoryStream())
                        {
                            stream.CopyTo(memFile);
                            return memFile.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ClientDependencySettings.Instance.Logger.Error("Error reading ClientDependency file from s3 bucket " + file, ex);
                throw new Exception("Error reading ClientDependency file from s3 bucket " + url, ex);
            }
        }
    }
}
