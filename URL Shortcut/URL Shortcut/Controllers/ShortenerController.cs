﻿using Cassandra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using URL_Shortcut.Models;
using URL_Shortcut.Models.POCOs;
using URL_Shortcut.Utils;
using URL_Shortcut.Utils.Database;
using URL_Shortcut.Utils.Network;

namespace URL_Shortcut.Controllers
{
    [Route("api/shortener")]
    public class ShortenerController : Controller
    {
        [Route("")]
        [HttpPost]
        public object Index([FromForm]string url)
        {
            // Create object to return
            APIResult result = new APIResult()
            {
                Status = (int)APIStatus.Failure,
                Message = "",
                URL = url,
                Shortcut = string.Empty,
                Popularity = 0
            };

            // Get the URL's SHA512 & SHA256 hash
            string sha512 = SHA512Hash.GetSHA512Hash(url);
            string sha256 = SHA256Hash.GetSHA256Hash(url);

            // Open up a connection to Cassandra
            CassandraConnection csConnection = new CassandraConnection();

            // Get connection session
            ISession csSession = csConnection.GetSession();

            // Lookup database and return the URL's signature if it exists
            SignatureLookup signatureLookup = new SignatureLookup(csSession);
            if (signatureLookup.LookupSignature(sha512, sha256, out string signature, out long hits))
            {
                result.Shortcut = this.MakeShortcut(signature);
                result.Popularity = hits;
                result.Status = (int)APIStatus.Success;
                return Json(result);
            }

            // Get total URL count from the running service
            long id = 0;
            try
            {
                const string BOT = "<~BOT~>";
                const string EOT = "<~EOT~>";
                const string COMMAND_COUNT = "COUNT";

                string message = string.Format("{0}{1}{2}", BOT, COMMAND_COUNT, EOT);
                string ip = "127.0.0.1";
                int port = 7079;

                AsyncClientSocket asyncClientSocket = new AsyncClientSocket();
                asyncClientSocket.Transmit(ip, port, message, out string response);
                //SyncClientSocket.Transmit(ip, port, message, out string response);

                if (response == string.Empty)
                {
                    result.Message = "Service Unavailable!";
                    return Json(result);
                }

                id = long.Parse(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            // Prepare dictionary
            char[] dictionary =
            {
                'A', 'B', 'C', 'D', 'E', 'F',
                'G', 'H', 'I', 'J', 'K', 'L',
                'M', 'N', 'O', 'P', 'Q', 'R',
                'S', 'T', 'U', 'V', 'W', 'X',
                'Y', 'Z',

                'a', 'b', 'c', 'd', 'e', 'f',
                'g', 'h', 'i', 'j', 'k', 'l',
                'm', 'n', 'o', 'p', 'q', 'r',
                's', 't', 'u', 'v', 'w', 'x',
                'y', 'z',

                '0', '1', '2', '3', '4', '5',
                '6', '7', '8', '9'
            };

            // Get signature
            signature = BaseN.ChangeBase(id, dictionary);

            // Unique signature is now set
            result.Shortcut = this.MakeShortcut(signature);

            // Insert the new URL into the database
            URLInsertion urlInsertion = new URLInsertion(csSession);
            if (urlInsertion.InsertURL(url, signature, sha512, sha256))
            {
                result.Popularity = 1;
                result.Status = (int)APIStatus.Success;
            }

            return Json(result);
        }

        private string MakeShortcut(string signature)
        {
            // Read app's base URL
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(@"Properties\launchSettings.json")
                .Build();
            string baseUrl = configuration["iisSettings:iisExpress:applicationUrl"];

            // Make the final shortcut URL
            string shortcut = string.Format("{0}?k={1}", baseUrl, signature);

            return shortcut;
        }

        private enum APIStatus
        {
            Failure = -1,
            Success = 0
        }
    }
}
