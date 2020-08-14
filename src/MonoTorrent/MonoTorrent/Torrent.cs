//
// Torrent.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using MonoTorrent.BEncoding;

namespace MonoTorrent
{
    public sealed class Torrent : IEquatable<Torrent>
    {
        static DateTime UnixEpoch => new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The announce URLs contained within the .torrent file
        /// </summary>
        public IList<IList<string>> AnnounceUrls { get; private set; }

        /// <summary>
        /// The comment contained within the .torrent file
        /// </summary>
        public string Comment { get; private set; }

        /// <summary>
        /// The optional string showing who/what created the .torrent
        /// </summary>
        public string CreatedBy { get; private set; }

        /// <summary>
        /// The creation date of the .torrent file
        /// </summary>
        public DateTime CreationDate { get; private set; }

        /// <summary>
        /// The optional ED2K hash contained within the .torrent file
        /// </summary>
        public byte[] ED2K { get; private set; }

        /// <summary>
        /// The encoding used by the client that created the .torrent file
        /// </summary>
        public string Encoding { get; private set; }

        /// <summary>
        /// The list of files contained within the .torrent which are available for download
        /// </summary>
        public IList<TorrentFile> Files { get; private set; }

        /// <summary>
        /// This is the http-based seeding (getright protocole)
        /// </summary>
        public IList<string> HttpSeeds { get; }

        /// <summary>
        /// This is the infohash that is generated by putting the "Info" section of a .torrent
        /// through a ManagedSHA1 hasher.
        /// </summary>
        public InfoHash InfoHash { get; private set; }

        /// <summary>
        /// The 'info' dictionary encoded as a byte array.
        /// </summary>
        internal byte[] InfoMetadata { get; private set; }

        /// <summary>
        /// Shows whether DHT is allowed or not. If it is a private torrent, no peer
        /// sharing should be allowed.
        /// </summary>
        public bool IsPrivate { get; private set; }

        /// <summary>
        /// In the case of a single file torrent, this is the name of the file.
        /// In the case of a multi file torrent, it is the name of the root folder.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The list of DHT nodes which can be used to bootstrap this torrent.
        /// </summary>
        public BEncodedList Nodes { get; private set; }

        /// <summary>
        /// The length of each piece in bytes.
        /// </summary>
        public int PieceLength { get; private set; }

        /// <summary>
        /// This is the array of hashes contained within the torrent.
        /// </summary>
        public Hashes Pieces { get; private set; }

        /// <summary>
        /// The name of the Publisher
        /// </summary>
        public string Publisher { get; private set; }

        /// <summary>
        /// The Url of the publisher of either the content or the .torrent file
        /// </summary>
        public string PublisherUrl { get; private set; }

        /// <summary>
        /// The optional SHA1 hash contained within the .torrent file
        /// </summary>
        public byte[] SHA1 { get; private set; }

        /// <summary>
        /// The source of the torrent
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// The size of all files in bytes.
        /// </summary>
        public long Size { get; private set; }

        Torrent ()
        {
            Comment = string.Empty;
            CreatedBy = string.Empty;
            CreationDate = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Encoding = string.Empty;
            Name = string.Empty;
            Publisher = string.Empty;
            PublisherUrl = string.Empty;
            HttpSeeds = new List<string> ();
        }

        public override bool Equals (object obj)
            => Equals (obj as Torrent);

        public bool Equals (Torrent other)
            => InfoHash == other?.InfoHash;

        public override int GetHashCode ()
            => InfoHash.GetHashCode ();

        public override string ToString ()
            => Name;

        /// <summary>
        /// This method is called internally to read out the hashes from the info section of the
        /// .torrent file.
        /// </summary>
        /// <param name="data">The byte[]containing the hashes from the .torrent file</param>
        void LoadHashPieces (byte[] data)
        {
            if (data.Length % 20 != 0)
                throw new TorrentException ("Invalid infohash detected");

            Pieces = new Hashes (data, data.Length / 20);
        }

        IList<TorrentFile> LoadTorrentFiles (BEncodedList list)
        {
            int endIndex;
            int startIndex;
            var sb = new StringBuilder (32);

            var files = new List<TorrentFile> ();
            foreach (BEncodedDictionary dict in list) {
                long length = 0;
                string path = null;
                byte[] md5sum = null;
                byte[] ed2k = null;
                byte[] sha1 = null;

                foreach (KeyValuePair<BEncodedString, BEncodedValue> keypair in dict) {
                    switch (keypair.Key.Text) {
                        case ("sha1"):
                            sha1 = ((BEncodedString) keypair.Value).TextBytes;
                            break;

                        case ("ed2k"):
                            ed2k = ((BEncodedString) keypair.Value).TextBytes;
                            break;

                        case ("length"):
                            length = long.Parse (keypair.Value.ToString ());
                            break;

                        case ("path.utf-8"):
                            foreach (BEncodedString str in ((BEncodedList) keypair.Value)) {
                                sb.Append (str.Text);
                                sb.Append (Path.DirectorySeparatorChar);
                            }
                            path = sb.ToString (0, sb.Length - 1);
                            sb.Remove (0, sb.Length);
                            break;

                        case ("path"):
                            if (string.IsNullOrEmpty (path)) {
                                foreach (BEncodedString str in ((BEncodedList) keypair.Value)) {
                                    sb.Append (str.Text);
                                    sb.Append (Path.DirectorySeparatorChar);
                                }
                                path = sb.ToString (0, sb.Length - 1);
                                sb.Remove (0, sb.Length);
                            }
                            break;

                        case ("md5sum"):
                            md5sum = ((BEncodedString) keypair.Value).TextBytes;
                            break;

                        default:
                            break; //FIXME: Log unknown values
                    }
                }

                // A zero length file always belongs to the same piece as the previous file
                if (length == 0) {
                    if (files.Count > 0) {
                        startIndex = files[files.Count - 1].EndPieceIndex;
                        endIndex = files[files.Count - 1].EndPieceIndex;
                    } else {
                        startIndex = 0;
                        endIndex = 0;
                    }
                } else {
                    startIndex = (int) (Size / PieceLength);
                    endIndex = (int) ((Size + length) / PieceLength);
                    if ((Size + length) % PieceLength == 0)
                        endIndex--;
                }

                PathValidator.Validate (path);
                files.Add (new TorrentFile (path, length, startIndex, endIndex, (int) (Size % PieceLength), md5sum, ed2k, sha1));
                Size += length;
            }

            return files.AsReadOnly ();
        }


        /// <summary>
        /// This method is called internally to load the information found within the "Info" section
        /// of the .torrent file
        /// </summary>
        /// <param name="dictionary">The dictionary representing the Info section of the .torrent file</param>
        void ProcessInfo (BEncodedDictionary dictionary)
        {
            InfoMetadata = dictionary.Encode ();
            PieceLength = int.Parse (dictionary["piece length"].ToString ());
            LoadHashPieces (((BEncodedString) dictionary["pieces"]).TextBytes);

            foreach (KeyValuePair<BEncodedString, BEncodedValue> keypair in dictionary) {
                switch (keypair.Key.Text) {
                    case ("source"):
                        Source = keypair.Value.ToString ();
                        break;

                    case ("sha1"):
                        SHA1 = ((BEncodedString) keypair.Value).TextBytes;
                        break;

                    case ("ed2k"):
                        ED2K = ((BEncodedString) keypair.Value).TextBytes;
                        break;

                    case ("publisher-url.utf-8"):
                        if (keypair.Value.ToString ().Length > 0)
                            PublisherUrl = keypair.Value.ToString ();
                        break;

                    case ("publisher-url"):
                        if ((string.IsNullOrEmpty (PublisherUrl)) && (keypair.Value.ToString ().Length > 0))
                            PublisherUrl = keypair.Value.ToString ();
                        break;

                    case ("publisher.utf-8"):
                        if (keypair.Value.ToString ().Length > 0)
                            Publisher = keypair.Value.ToString ();
                        break;

                    case ("publisher"):
                        if ((string.IsNullOrEmpty (Publisher)) && (keypair.Value.ToString ().Length > 0))
                            Publisher = keypair.Value.ToString ();
                        break;

                    case ("files"):
                        Files = LoadTorrentFiles ((BEncodedList) keypair.Value);
                        break;

                    case ("name.utf-8"):
                        if (keypair.Value.ToString ().Length > 0)
                            Name = keypair.Value.ToString ();
                        break;

                    case ("name"):
                        if ((string.IsNullOrEmpty (Name)) && (keypair.Value.ToString ().Length > 0))
                            Name = keypair.Value.ToString ();
                        break;

                    case ("piece length"):  // Already handled
                        break;

                    case ("length"):
                        break;      // This is a singlefile torrent

                    case ("private"):
                        IsPrivate = (keypair.Value.ToString () == "1") ? true : false;
                        break;

                    default:
                        break;
                }
            }

            if (Files == null)   // Not a multi-file torrent
            {
                long length = long.Parse (dictionary["length"].ToString ());
                Size = length;
                string path = Name;
                byte[] md5 = (dictionary.ContainsKey ("md5")) ? ((BEncodedString) dictionary["md5"]).TextBytes : null;
                byte[] ed2k = (dictionary.ContainsKey ("ed2k")) ? ((BEncodedString) dictionary["ed2k"]).TextBytes : null;
                byte[] sha1 = (dictionary.ContainsKey ("sha1")) ? ((BEncodedString) dictionary["sha1"]).TextBytes : null;

                int endPiece = Math.Min (Pieces.Count - 1, (int) ((Size + (PieceLength - 1)) / PieceLength));
                Files = Array.AsReadOnly (new[] { new TorrentFile (path, length, 0, endPiece, 0, md5, ed2k, sha1) });
            }
        }

        /// <summary>
        /// This method loads a .torrent file from the specified path.
        /// </summary>
        /// <param name="path">The path to load the .torrent file from</param>
        public static Torrent Load (string path)
        {
            Check.Path (path);

            using Stream s = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Load (s, path);
        }

        /// <summary>
        /// This method loads a .torrent file from the specified path.
        /// </summary>
        /// <param name="path">The path to load the .torrent file from</param>
        public static Task<Torrent> LoadAsync (string path)
        {
            return Task.Run (() => Load (path));
        }

        /// <summary>
        /// Loads a torrent from a byte[] containing the bencoded data
        /// </summary>
        /// <param name="data">The byte[] containing the data</param>
        /// <returns></returns>
        public static Torrent Load (byte[] data)
        {
            Check.Data (data);

            using var s = new MemoryStream (data);
            return Load (s, "");
        }

        /// <summary>
        /// Loads a torrent from a byte[] containing the bencoded data
        /// </summary>
        /// <param name="data">The byte[] containing the data</param>
        /// <returns></returns>
        public static Task<Torrent> LoadAsync (byte[] data)
        {
            return Task.Run (() => Load (data));
        }

        /// <summary>
        /// Loads a .torrent from the supplied stream
        /// </summary>
        /// <param name="stream">The stream containing the data to load</param>
        /// <returns></returns>
        public static Torrent Load (Stream stream)
        {
            Check.Stream (stream);

            if (stream == null)
                throw new ArgumentNullException (nameof (stream));

            return Load (stream, "");
        }

        /// <summary>
        /// Loads a .torrent from the supplied stream
        /// </summary>
        /// <param name="stream">The stream containing the data to load</param>
        /// <returns></returns>
        public static Task<Torrent> LoadAsync (Stream stream)
        {
            return Task.Run (() => Load (stream));
        }

        /// <summary>
        /// Loads a .torrent file from the specified URL
        /// </summary>
        /// <param name="url">The URL to download the .torrent from</param>
        /// <param name="location">The path to download the .torrent to before it gets loaded</param>
        /// <returns></returns>
        public static Torrent Load (Uri url, string location)
        {
            Check.Url (url);
            Check.Location (location);

            try {
                using var client = new WebClient ();
                client.DownloadFile (url, location);
            } catch (Exception ex) {
                File.Delete (location);
                throw new TorrentException ("Could not download .torrent file from the specified url", ex);
            }

            return Load (location);
        }

        /// <summary>
        /// Loads a .torrent file from the specified URL
        /// </summary>
        /// <param name="url">The URL to download the .torrent from</param>
        /// <param name="location">The path to download the .torrent to before it gets loaded</param>
        /// <returns></returns>
        public static async Task<Torrent> LoadAsync (Uri url, string location)
        {
            try {
                using var client = new WebClient ();
                await client.DownloadFileTaskAsync (url, location).ConfigureAwait (false);
            } catch (Exception ex) {
                File.Delete (location);
                throw new TorrentException ("Could not download .torrent file from the specified url", ex);
            }

            return await LoadAsync (location).ConfigureAwait (false);
        }

        /// <summary>
        /// Loads a .torrent from the specificed path. A return value indicates
        /// whether the operation was successful.
        /// </summary>
        /// <param name="path">The path to load the .torrent file from</param>
        /// <param name="torrent">If the loading was succesful it is assigned the Torrent</param>
        /// <returns>True if successful</returns>
        public static bool TryLoad (string path, out Torrent torrent)
        {
            Check.Path (path);

            torrent = null;
            try {
                if (!string.IsNullOrEmpty (path) && File.Exists (path))
                    torrent = Load (path);
            } catch {
                // We will return false if an exception is thrown as 'torrent' will still
                // be null.
            }

            return torrent != null;
        }

        /// <summary>
        /// Loads a .torrent from the specified byte[]. A return value indicates
        /// whether the operation was successful.
        /// </summary>
        /// <param name="data">The byte[] to load the .torrent from</param>
        /// <param name="torrent">If loading was successful, it contains the Torrent</param>
        /// <returns>True if successful</returns>
        public static bool TryLoad (byte[] data, out Torrent torrent)
        {
            Check.Data (data);

            try {
                torrent = Load (data);
            } catch {
                torrent = null;
            }

            return torrent != null;
        }

        /// <summary>
        /// Loads a .torrent from the supplied stream. A return value indicates
        /// whether the operation was successful.
        /// </summary>
        /// <param name="stream">The stream containing the data to load</param>
        /// <param name="torrent">If the loading was succesful it is assigned the Torrent</param>
        /// <returns>True if successful</returns>
        public static bool TryLoad (Stream stream, out Torrent torrent)
        {
            Check.Stream (stream);

            try {
                torrent = Load (stream);
            } catch {
                torrent = null;
            }

            return torrent != null;
        }

        /// <summary>
        /// Loads a .torrent file from the specified URL. A return value indicates
        /// whether the operation was successful.
        /// </summary>
        /// <param name="url">The URL to download the .torrent from</param>
        /// <param name="location">The path to download the .torrent to before it gets loaded</param>
        /// <param name="torrent">If the loading was succesful it is assigned the Torrent</param>
        /// <returns>True if successful</returns>
        public static bool TryLoad (Uri url, string location, out Torrent torrent)
        {
            Check.Url (url);
            Check.Location (location);

            try {
                torrent = Load (url, location);
            } catch {
                torrent = null;
            }

            return torrent != null;
        }

        /// <summary>
        /// Called from either Load(stream) or Load(string).
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        static Torrent Load (Stream stream, string path)
        {
            Check.Stream (stream);
            Check.Path (path);

            try {
                var torrent = LoadCore ((BEncodedDictionary) BEncodedValue.Decode (stream));
                torrent.Source = path;
                return torrent;
            } catch (BEncodingException ex) {
                throw new TorrentException ("Invalid torrent file specified", ex);
            }
        }

        public static Torrent Load (BEncodedDictionary torrentInformation)
        {
            return LoadCore ((BEncodedDictionary) BEncodedValue.Decode (torrentInformation.Encode ()));
        }

        internal static Torrent LoadCore (BEncodedDictionary torrentInformation)
        {
            Check.TorrentInformation (torrentInformation);

            var t = new Torrent ();
            t.LoadInternal (torrentInformation);

            return t;
        }

        void LoadInternal (BEncodedDictionary torrentInformation)
        {
            Check.TorrentInformation (torrentInformation);
            AnnounceUrls = new List<IList<string>> ().AsReadOnly ();

            foreach (KeyValuePair<BEncodedString, BEncodedValue> keypair in torrentInformation) {
                switch (keypair.Key.Text) {
                    case ("announce"):
                        // Ignore this if we have an announce-list
                        if (torrentInformation.ContainsKey ("announce-list"))
                            break;
                        AnnounceUrls = new List<IList<string>> {
                            new List<string> { keypair.Value.ToString () }.AsReadOnly ()
                        }.AsReadOnly ();
                        break;

                    case ("creation date"):
                        try {
                            try {
                                CreationDate = UnixEpoch.AddSeconds (long.Parse (keypair.Value.ToString ()));
                            } catch (Exception e) {
                                if (e is ArgumentOutOfRangeException)
                                    CreationDate = UnixEpoch.AddMilliseconds (long.Parse (keypair.Value.ToString ()));
                                else
                                    throw;
                            }
                        } catch (Exception e) {
                            if (e is ArgumentOutOfRangeException)
                                throw new BEncodingException ("Argument out of range exception when adding seconds to creation date.", e);
                            else if (e is FormatException)
                                throw new BEncodingException ($"Could not parse {keypair.Value} into a number", e);
                            else
                                throw;
                        }
                        break;

                    case ("nodes"):
                        if (keypair.Value is BEncodedList list)
                            Nodes = list;
                        break;

                    case ("comment.utf-8"):
                        if (keypair.Value.ToString ().Length != 0)
                            Comment = keypair.Value.ToString ();       // Always take the UTF-8 version
                        break;                                          // even if there's an existing value

                    case ("comment"):
                        if (string.IsNullOrEmpty (Comment))
                            Comment = keypair.Value.ToString ();
                        break;

                    case ("publisher-url.utf-8"):                       // Always take the UTF-8 version
                        PublisherUrl = keypair.Value.ToString ();      // even if there's an existing value
                        break;

                    case ("publisher-url"):
                        if (string.IsNullOrEmpty (PublisherUrl))
                            PublisherUrl = keypair.Value.ToString ();
                        break;

                    case ("created by"):
                        CreatedBy = keypair.Value.ToString ();
                        break;

                    case ("encoding"):
                        Encoding = keypair.Value.ToString ();
                        break;

                    case ("info"):
                        using (SHA1 s = HashAlgoFactory.SHA1 ())
                            InfoHash = new InfoHash (s.ComputeHash (keypair.Value.Encode ()));
                        ProcessInfo (((BEncodedDictionary) keypair.Value));
                        break;

                    case ("name"):                                               // Handled elsewhere
                        break;

                    case ("announce-list"):
                        if (keypair.Value is BEncodedString)
                            break;

                        var result = new List<IList<string>> ();
                        var announces = (BEncodedList) keypair.Value;
                        for (int j = 0; j < announces.Count; j++) {
                            if (announces[j] is BEncodedList bencodedTier) {
                                var tier = new List<string> (bencodedTier.Count);

                                for (int k = 0; k < bencodedTier.Count; k++)
                                    tier.Add (bencodedTier[k].ToString ());

                                Toolbox.Randomize (tier);

                                var resultTier = new List<string> ();
                                for (int k = 0; k < tier.Count; k++)
                                    resultTier.Add (tier[k]);

                                if (resultTier.Count != 0)
                                    result.Add (tier.AsReadOnly ());
                            } else {
                                throw new BEncodingException (
                                    $"Non-BEncodedList found in announce-list (found {announces[j].GetType ()})");
                            }
                        }
                        if (result.Count > 0)
                            AnnounceUrls = result.AsReadOnly ();
                        break;

                    case ("httpseeds"):
                        // This form of web-seeding is not supported.
                        break;

                    case ("url-list"):
                        if (keypair.Value is BEncodedString httpSeedString) {
                            HttpSeeds.Add (httpSeedString.Text);
                        } else if (keypair.Value is BEncodedList httpSeedList) {
                            foreach (BEncodedString str in httpSeedList)
                                HttpSeeds.Add (str.Text);
                        }
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
