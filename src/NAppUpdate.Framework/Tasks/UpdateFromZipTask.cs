using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using NAppUpdate.Framework.Common;

namespace NAppUpdate.Framework.Tasks
{
    [UpdateTaskAlias("zipUpdate")]
    class UpdateFromZipTask : IUpdateTask
    {

        public UpdateFromZipTask()
        {
            UpdateConditions = new Conditions.BooleanCondition();
        }


        [NauField("updateTo",
            "File name on the remote location; same name as local path will be used if left blank"
            , true)]
        public string UpdateTo { get; set; }

        [NauField("sha256-checksum", "SHA-256 checksum to validate the file after download (optional)", false)]
        public string Sha256Checksum { get; set; }

        internal string updateDirectory = Path.Combine(UpdateManager.Instance.TempFolder, Guid.NewGuid().ToString());
        private string destinationPath;
        private List<string> filesList;

        public string Description { get; set; }

        public Conditions.BooleanCondition UpdateConditions { get; set; }

        /// <summary>
        /// Do all work, especially if it is lengthy, required to prepare the update task, except from
        /// the final trivial operations required to actually perform the update.
        /// </summary>
        /// <param name="source">An update source object, in case more data is required</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool Prepare(Sources.IUpdateSource source)
        {
            if (string.IsNullOrEmpty(UpdateTo))
                return false;

            // Clear temp folder
            if (Directory.Exists(updateDirectory))
            {
                try
                {
                    Directory.Delete(updateDirectory, true);
                }
                catch { }
            }

            Directory.CreateDirectory(updateDirectory);

            // Download the zip to a temp file that is deleted automatically when the app exits
            string zipLocation = null;
            try
            {
                if (!source.GetData(UpdateTo, string.Empty, ref zipLocation))
                    return false;
            }
            catch (Exception ex)
            {
                throw new UpdateProcessFailedException("Couldn't get Data from source", ex);
            }
            if (!string.IsNullOrEmpty(Sha256Checksum))
            {
                string checksum = Utils.FileChecksum.GetSHA256Checksum(zipLocation);
                if (!checksum.Equals(Sha256Checksum))
                    return false;
            }

            if (string.IsNullOrEmpty(zipLocation))
                return false;

            // Unzip to temp folder; no need to delete the zip file as this will be done by the OS
            filesList = new List<string>();
            ZipFile zf = null;
            try
            {
                FileStream fs = File.OpenRead(zipLocation);
                zf = new ZipFile(fs);
                foreach (ZipEntry zipEntry in zf)
                {
                    if (!zipEntry.IsFile)
                    {
                        continue;			// Ignore directories
                    }
                    String entryFileName = zipEntry.Name;
                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                    // Optionally match entrynames against a selection list here to skip as desired.
                    // The unpacked length is available in the zipEntry.Size property.

                    byte[] buffer = new byte[4096];		// 4K is optimum
                    Stream zipStream = zf.GetInputStream(zipEntry);

                    // Manipulate the output filename here as desired.
                    String fullZipToPath = Path.Combine(updateDirectory, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (directoryName.Length > 0)
                        Directory.CreateDirectory(directoryName);

                    // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                    // of the file, but does not waste memory.
                    // The "using" will close the stream even if an exception occurs.
                    using (FileStream streamWriter = File.Create(fullZipToPath))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                    filesList.Add(entryFileName);
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new UpdateProcessFailedException("Couldn't get unzip data", ex);
            }
            finally
            {
                if (zf != null)
                {
                    zf.IsStreamOwner = true; // Makes close also shut the underlying stream
                    zf.Close(); // Ensure we release resources
                }
            }


        }

        /// <summary>
        /// Execute the update. After all preparation is done, this call should be quite a short one
        /// to perform.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool Execute()
        {
            return true;
        }
        public IEnumerator<KeyValuePair<string, object>> GetColdUpdates()
        {
            if (filesList == null)
                yield break;
 
            foreach (var file in filesList)
            {
                yield return new KeyValuePair<string, object>(file, Path.Combine(updateDirectory, file));
            }

        }

        /// <summary>
        /// Rollback the update performed by this task.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool Rollback()
        {
            return true;
        }
    }
}
