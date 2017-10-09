using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

using System.Configuration;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace ClientDependency.Core.CompositeFiles.Providers
{

    /// <summary>
    /// Creates an XML file to map a saved composite file to the URL requested for the 
    /// dependency handler. 
    /// This is used in order to determine which individual files are dependant on what composite file so 
    /// a user can remove it to clear the cache, and also if the cache expires but the file still exists
    /// this allows the system to simply read the one file again instead of compiling all of the other files
    /// into one again.
    /// </summary>
    public class RedisXmlFileMapper : BaseFileMapProvider
    {
        #region redis cache
        // Redis Connection string info
        private static string cacheConnection = ConfigurationManager.AppSettings["RedisCacheConnection"];
        private static int clientDependencyRedisDatabase = -1;
        private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            if (string.IsNullOrEmpty(cacheConnection))
                return null;

            return ConnectionMultiplexer.Connect(cacheConnection);
        });
        public static ConnectionMultiplexer Connection
        {
            get
            {
                if (string.IsNullOrEmpty(cacheConnection))
                    return null;

                return lazyConnection.Value;
            }
        }

        public static IDatabase RedisDatabase()
        {
            return Connection.GetDatabase(clientDependencyRedisDatabase);
        }

        #endregion
        
        private const string FileMapVirtualFolderDefault = "~/ClientDependency/Data";
        /// <summary>
        /// Specifies the default folder to store the file map in, this allows for dynamically changing the folder on startup
        /// </summary>
        public static string FileMapVirtualFolder = FileMapVirtualFolderDefault;

        /// <summary>
        /// Initializes the provider
        /// </summary>
        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(name, config);

            if (config == null)
                return;

            if (config["mapPath"] != null)
            {
                //use the config setting if it has not been dynamically set OR
                //when the config section doesn't equal the default
                if (FileMapVirtualFolder == FileMapVirtualFolderDefault
                    || config["mapPath"] != FileMapVirtualFolderDefault)
                {
                    FileMapVirtualFolder = config["mapPath"];
                }
            }

            if (config["dbNum"] != null)
            {
                // if the database to use is specify
                int dbNum = -1;
                if (int.TryParse(config["dbNum"], out dbNum))
                {
                    clientDependencyRedisDatabase = dbNum;
                }
            }

            if (config["redisCacheConnection"] != null)
            {
                if (!string.IsNullOrEmpty(config["redisCacheConnection"]))
                {
                    cacheConnection = config["redisCacheConnection"];
                }
            }
        }
        
        #region abstract implementations

        public override void Initialize(HttpContextBase http)
        {
            if (http == null) throw new ArgumentNullException("http");
        }

        /// <summary>
        /// Returns the composite file map associated with the file key, the version and the compression type
        /// </summary>
        /// <param name="fileKey"></param>
        /// <param name="version"></param>
        /// <param name="compression"></param>
        /// <returns></returns>
        public override CompositeFileMap GetCompositeFile(string fileKey, int version, string compression)
        {
            if (string.IsNullOrEmpty(fileKey)) throw new ArgumentNullException("fileKey");
            
            var x = FindItem(fileKey, version, compression);
            if (x == null)
                return null;
            try
            {
                return x.ToCompositeFileMap();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retreives the dependent file paths for the filekey/version (regardless of compression)
        /// </summary>
        /// <param name="fileKey"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public override IEnumerable<string> GetDependentFiles(string fileKey, int version)
        {
            if (string.IsNullOrEmpty(fileKey)) throw new ArgumentNullException("fileKey");
            
            var x = FindItem(fileKey, version);
            try
            {
                if (x != null)
                {
                    return x.DependentFiles;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        /// <summary>
        /// Creates a new file map and file key for the dependent file list, this is used to create URLs with CompositeUrlType.MappedId 
        /// </summary>
        ///<example>
        /// <![CDATA[
        /// <map>
        ///		<item key="123xsy" 
        ///			file=""
        ///			compresion="deflate"
        ///         version="1234">
        ///			<files>
        ///				<file name="C:\asdf\JS\jquery.js" />
        ///				<file name="C:\asdf\JS\jquery.ui.js" />		
        ///			</files>
        ///		</item>
        /// </map>
        /// ]]>
        /// </example>
        public override string CreateNewMap(HttpContextBase http,
            IEnumerable<IClientDependencyFile> dependentFiles,
            int version)
        {
            if (http == null) throw new ArgumentNullException("http");

            var builder = new StringBuilder();
            foreach (var d in dependentFiles)
            {
                builder.Append(d.FilePath);
                builder.Append(";");
            }
            var combinedFiles = builder.ToString();
            combinedFiles = combinedFiles.TrimEnd(new[] { ';' });

            var fileKey = (combinedFiles + version).GenerateHash();

            var x = FindItem(fileKey, version);

            //if no map exists, create one
            if (x == null)
            {
                //now, create a map with the file key so that it can be filled out later with the actual composite file that is created by the handler
                CreateUpdateMap(fileKey,
                    string.Empty,
                    dependentFiles,
                    string.Empty,
                    version);
            }

            return fileKey;
        }

        /// <summary>
        /// Adds/Updates an entry to the file map with the key specified, the version and dependent files listed with a map
        /// to the composite file created for the files.
        /// </summary>
        /// <param name="fileKey"></param>
        ///<param name="compressionType"></param>
        ///<param name="dependentFiles"></param>
        /// <param name="compositeFile"></param>
        ///<param name="version"></param>
        ///<example>
        /// <![CDATA[
        /// <map>
        ///		<item key="XSDFSDKJHLKSDIOUEYWCDCDSDOIUPOIUEROIJDSFHG" 
        ///			file="C:\asdf\App_Data\ClientDependency\123456.cdj"
        ///			compresion="deflate"
        ///         version="1234">
        ///			<files>
        ///				<file name="C:\asdf\JS\jquery.js" />
        ///				<file name="C:\asdf\JS\jquery.ui.js" />		
        ///			</files>
        ///		</item>
        /// </map>
        /// ]]>
        /// </example>
        public override void CreateUpdateMap(string fileKey,
            string compressionType,
            IEnumerable<IClientDependencyFile> dependentFiles,
            string compositeFile,
            int version)
        {
            if (string.IsNullOrEmpty(fileKey)) throw new ArgumentNullException("fileKey");

            var cfile = new CompositeFileEntry(fileKey, 
                compressionType, 
                compositeFile, 
                dependentFiles.Select(x => x.FilePath).ToList(), 
                version);

            var db = RedisDatabase();
            db.StringSet(GetCompositeFileEntryIndexKey(fileKey, compressionType), JsonConvert.SerializeObject(cfile));
        }

        #endregion
        
        /// <summary>
        /// Finds a element in the map matching key/version
        /// </summary>
        /// <param name="key"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        private CompositeFileEntry FindItem(string key, int version, string compression = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException("key");

            var db = RedisDatabase();
            // version is already encoded in the key
            string json = db.StringGet(GetCompositeFileEntryIndexKey(key, compression));
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            var item = JsonConvert.DeserializeObject<CompositeFileEntry>(json);
            // just for sanity
            if (item.Version == version)
            {
                return item;
            }
            else
            {
                return null;
            }
        }

        private string GetCompositeFileEntryIndexKey(string key, string compression)
        {
            string c = "";
            if (!string.IsNullOrEmpty(compression))
            {
                c = "." + compression;
            }
            return string.Format("{0}/Maps/{1}.json{2}", FileMapVirtualFolder, key, c);
        }
    }

    public class CompositeFileEntry
    {
        public CompositeFileEntry(string key, string compressionType, string file, IEnumerable<string> filePaths, int version)
        {
            DependentFiles = filePaths;
            FileKey = key;
            CompositeFileName = file;
            CompressionType = compressionType;
            Version = version;
        }

        [JsonProperty]
        public string FileKey { get; private set; }
        [JsonProperty]
        public string CompositeFileName { get; private set; }
        [JsonProperty]
        public string CompressionType { get; private set; }
        [JsonProperty]
        public int Version { get; private set; }
        [JsonProperty]
        public IEnumerable<string> DependentFiles { get; private set; }

        private byte[] m_FileBytes;

        /// <summary>
        /// If for some reason the file doesn't exist any more or we cannot read the file, this will return false.
        /// </summary>
        public bool HasFileBytes
        {
            get
            {
                GetCompositeFileBytes();
                return m_FileBytes != null;
            }
        }

        /// <summary>
        /// Returns the file's bytes
        /// </summary>
        public byte[] GetCompositeFileBytes()
        {
            // if not using S3 return null so the file gets processed
            if (!CompositeS3FileProcessorProvider.HasS3Settings)
                return null;

            if (m_FileBytes == null)
            {
                if (string.IsNullOrEmpty(CompositeFileName))
                {
                    return null;
                }

                try
                {
                    m_FileBytes = CompositeS3FileProcessorProvider.ReadS3File(CompositeFileName);
                }
                catch
                {
                    m_FileBytes = null;
                }
            }
            return m_FileBytes;
        }

        /// <summary>
        /// this method is created becuase for some odd reason the constructor in the CompositeFileMap is internal
        /// </summary>
        /// <returns></returns>
        public CompositeFileMap ToCompositeFileMap()
        {
            return JsonConvert.DeserializeObject<CompositeFileMap>(JsonConvert.SerializeObject(this));
        }
    }

   
}
