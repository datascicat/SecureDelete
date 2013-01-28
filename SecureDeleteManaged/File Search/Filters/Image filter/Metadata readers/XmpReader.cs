// Copyright (c) 2007 Gratian Lup. All rights reserved.
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
// * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//
// * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following
// disclaimer in the documentation and/or other materials provided
// with the distribution.
//
// * The name "SecureDelete" must not be used to endorse or promote
// products derived from this software without prior written permission.
//
// * Products derived from this software may not be called "SecureDelete" nor
// may "SecureDelete" appear in their names without prior written
// permission of the author.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using DebugUtils.Debugger;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml;

namespace SecureDelete.FileSearch {
    public class XmpReader {
        #region Fields

        private static XmlNamespaceManager namespaceManager;

        #endregion

        #region Private methods

        private static XmlDocument GetXmpDocument(string data) {
            if(data == null) {
                throw new ArgumentNullException("data");
            }

            try {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(data);
                return doc;
            }
            catch(Exception e) {
                Debug.ReportError("Error while parsing XMP document. Exception: {0}", e.Message);
                return null;
            }
        }


        private static void GenerateNamespaceManager(XmlDocument doc) {
            if(namespaceManager == null) {
                namespaceManager = new XmlNamespaceManager(doc.NameTable);
                namespaceManager.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                namespaceManager.AddNamespace("exif", "http://ns.adobe.com/exif/1.0/");
                namespaceManager.AddNamespace("x", "adobe:ns:meta/");
                namespaceManager.AddNamespace("xap", "http://ns.adobe.com/xap/1.0/");
                namespaceManager.AddNamespace("tiff", "http://ns.adobe.com/tiff/1.0/");
                namespaceManager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");

                // for image metadata generated by Windows Vista and Windows (Live) Gallery
                namespaceManager.AddNamespace("MicrosoftPhoto", "http://ns.microsoft.com/photo/1.0");
            }
        }


        private static string GetXmpXmlDocFromImage(FileStream stream) {
            if(stream == null) {
                throw new ArgumentNullException("stream");
            }

            string beginCapture = "<rdf:RDF";
            string endCapture = "</rdf:RDF>";
            bool beginFound = false;
            bool collecting = false;

            StringBuilder data = null;
            string temp = "";

            stream.Position = 0;
            StreamReader streamReader = new StreamReader(stream);

            while(!streamReader.EndOfStream) {
                char c = (char)streamReader.Read();

                if(!collecting) {
                    if(c == '<') {
                        // start character found
                        beginFound = true;
                    }

                    if(beginFound) {
                        temp += c;

                        if(temp.Length == beginCapture.Length && temp == beginCapture) {
                            collecting = true;
                            data = new StringBuilder(temp);
                        }
                        else if(temp.Length > beginCapture.Length) {
                            // invalid start, reset
                            temp = "";
                            beginFound = false;
                        }
                    }
                }
                else {
                    data.Append(c);

                    if(c == '>') {
                        // end character found
                        // check for end signature
                        if(data.ToString().EndsWith(endCapture)) {
                            // found, return XML data
                            return data.ToString();
                        }
                    }
                }
            }

            return null;
        }

        #endregion

        private static bool InitializeReader(ImageData data) {
            if(data.XmpDocument == null) {
                string xmpData = GetXmpXmlDocFromImage(data.Stream);

                if(xmpData == null || xmpData.Length == 0) {
                    return false;
                }

                data.XmpDocument = GetXmpDocument(xmpData);

                // generate the namespace manager
                if(data.XmpDocument != null) {
                    GenerateNamespaceManager(data.XmpDocument);
                }

                return data.XmpDocument != null;
            }
            else {
                return true;
            }
        }

        #region Public methods

        /// <summary>
        /// Rating
        /// </summary>
        public static int? GetRating(ImageData data) {
            if(InitializeReader(data) == false) {
                return null;
            }

            // extract the rating
            try {
                XmlNode node = data.XmpDocument.SelectSingleNode("/rdf:RDF/rdf:Description/xap:Rating", namespaceManager);
                if(node != null) {
                    return int.Parse(node.InnerText);
                }
                else {
                    // try to get it from Vista Photo Gallery
                    node = data.XmpDocument.SelectSingleNode("/rdf:RDF/rdf:Description", namespaceManager);

                    foreach(XmlAttribute attribute in node.Attributes) {
                        if(attribute.Name == "MicrosoftPhoto:Rating") {
                            // values are ranging from 0 to 100
                            // convert to 0 - 5 stars
                            double value = double.Parse(attribute.Value) / 100;
                            return (int)Math.Floor(value * 5.0);
                        }
                    }
                }
            }
            catch { }

            return null;
        }


        /// <summary>
        /// Tags
        /// </summary>
        public static List<string> GetTags(ImageData data) {
            if(InitializeReader(data) == false) {
                return null;
            }

            // extract the tags
            try {
                XmlNode node = data.XmpDocument.SelectSingleNode("/rdf:RDF/rdf:Description/dc:subject/rdf:Bag", namespaceManager);

                if(node != null) {
                    List<string> tags = new List<string>();

                    // copy the tags
                    foreach(XmlNode tag in node) {
                        tags.Add(tag.InnerText);
                    }

                    return tags;
                }
            }
            catch { }

            return null;
        }


        /// <summary>
        /// Title
        /// </summary>
        public static string GetTitle(ImageData data) {
            if(InitializeReader(data) == false) {
                return null;
            }

            // extract the title
            try {
                XmlNode node = data.XmpDocument.SelectSingleNode("/rdf:RDF/rdf:Description/dc:title/rdf:Alt", namespaceManager);

                if(node != null && node.ChildNodes.Count > 0) {
                    return node.ChildNodes[0].InnerText;
                }
            }
            catch { }

            return null;
        }


        /// <summary>
        /// Authors
        /// </summary>
        public static List<string> GetAuthors(ImageData data) {
            if(InitializeReader(data) == false) {
                return null;
            }

            // extract the tags
            try {
                XmlNode node = data.XmpDocument.SelectSingleNode("/rdf:RDF/rdf:Description/dc:creator/rdf:Seq", namespaceManager);

                if(node != null) {
                    List<string> authors = new List<string>();

                    // copy the tags
                    foreach(XmlNode author in node) {
                        authors.Add(author.InnerText);
                    }

                    return authors;
                }
            }
            catch { }

            return null;
        }

        #endregion
    }
}
