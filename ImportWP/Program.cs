using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Wordpress.Xml.Rpc;
using WordPressSharp;

namespace ImportWP
{
    class Program
    {
        public class WPRecord
        {
            public string PostType { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public DateTime PublishDateTime { get; set; }
            public string Status { get; set; }
        }

        static void Main(string[] args)
        {


            string filepath = @"C:\Users\Alexey\documents\visual studio 2017\Projects\ImportWP\ImportWP\data\allPostsSmall.xml";

            var xml = XDocument.Load(filepath, LoadOptions.None);
            var query = from c in xml.Root.Descendants("post")
                            //where Convert.ToDateTime(c.Attribute("date").Value) < DateTime.Parse("2016-01-01")
                        select c;
           
            List<WPRecord> wprecords = new List<WPRecord>();
            foreach (XElement title in query)
            {
                WPRecord wpr = new WPRecord();
                try
                {
                    wpr.PublishDateTime = Convert.ToDateTime(title.Attribute("date").Value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Неверный формат даты  " + ex.Message);
                }

                wpr.Title = title.Element("title").Value;
                wpr.Content = title.Element("body").Value;
                wpr.PostType = "post";
                wpr.Status = "publish";

                wpr.Content = wpr.Content.Replace("\n", "");

                string shortDate = wpr.PublishDateTime.ToString("yyyy-MM-dd");

                RegexOptions options = RegexOptions.None;
                Regex regex = new Regex("[ ]{2,}", options);
                wpr.Content = regex.Replace(wpr.Content, " ");



                //< !--more-- >

                //string ljCutRe = @"<lj-cut[^>]*?text\s*=\s*[""']?([^'"" >]+?)[ '""][^>]*?>";
                string ljCutPattern = @"<lj-cut\s*?text\s*=\s*[""']?([^'"">]+?)['""][^>]*?>";
                Regex ljCutRegex = new Regex(ljCutPattern, RegexOptions.IgnoreCase);
                wpr.Content = ljCutRegex.Replace(wpr.Content, "<!--more$1-->");

                wpr.Content = wpr.Content.Replace("</lj-cut>", "");

                //Console.WriteLine(wpr.Content);

                //MatchCollection ljcuts = Regex.Matches(wpr.Content, ljCutPattern, RegexOptions.IgnoreCase);
                //foreach (Match match in ljcuts)
                //{
                //    Console.WriteLine("0: " + match.Groups[0]);
                //    Console.WriteLine("1: " + match.Groups[1]);
                //    Console.WriteLine("2: " + match.Groups[2]);

                //}

                GetImagesFormAHrefLinks(wpr, shortDate);
                GetImagesFromImgSrcTag(wpr, shortDate);
                AddToWordPress(wpr);

            }




        }

        private static void GetImagesFromImgSrcTag(WPRecord wpr, string shortDate)
        {
            List<Uri> imagesFromSrc = FetchImagesFromSource(wpr.Content);
            foreach (Uri uri in imagesFromSrc)
            {
                string savedImage = SaveImage(uri, shortDate);

                if (!savedImage.Contains("Ошибка"))
                {
                    Console.WriteLine("Картинка: " + savedImage);
                    wpr.Content = wpr.Content.Replace(uri.ToString(), savedImage);
                }
                else
                {
                    Console.WriteLine("" + savedImage);
                }
            }
        }

        private static void GetImagesFormAHrefLinks(WPRecord wpr, string shortDate)
        {
            //Download original photos from hyerlinks found <a href=....
            List<Uri> imageLinks = FetchImagesFromLinksSource(wpr.Content);
            foreach (Uri uri in imageLinks)
            {
                string savedImage = SaveImage(uri, shortDate);

                if (!savedImage.Contains("Ошибка"))
                {
                    Console.WriteLine("Картинка: " + savedImage);
                    wpr.Content = wpr.Content.Replace(uri.ToString(), savedImage);
                }
                else
                {
                    Console.WriteLine("" + savedImage);
                }

                Console.WriteLine(uri);
            }
        }

        private static string  SaveImage(Uri uri, string shortDate)
        {
            string mainDirectory = @"C:\Sites\dm\wp-content\images\";
            string dateDir = mainDirectory + shortDate + @"\";
            string fileName = Path.GetFileName(uri.ToString());

            string destFile = dateDir + fileName;
            string webDir = "/wp-content/images/" + shortDate + "/" + fileName; 

            try
            {
                Directory.CreateDirectory(dateDir);
                WebClient wc = new WebClient();
                wc.DownloadFile(uri, destFile);
            }
            catch(Exception ex)
            {
                webDir = "Ошибка!  " + ex.Message;
            }
            return webDir;
            //byte[] imageBytes = wc.DownloadData(uri);

            //throw new NotImplementedException();
        }

        private static void AddToWordPress(WPRecord wpr)
        {
            var post = new WordPressSharp.Models.Post
            {
                PostType = wpr.PostType,
                Title = wpr.Title,
                Content = wpr.Content,
                PublishDateTime = wpr.PublishDateTime,
                Status = wpr.Status // "draft" or "publish"
            };

            using (var client = new WordPressClient(new WordPressSiteConfig
            {
                BaseUrl = "http://dm",
                Username = "admin",
                Password = "mypass"
            }))
            {
                var id = Convert.ToInt32(client.NewPost(post));
                Console.WriteLine(id);
            }
        }



        public  static List<Uri> FetchImagesFromSource(string htmlSource)
        {
            List<Uri> links = new List<Uri>();
            string regexImgSrc = @"<img[^>]*?src\s*=\s*[""']?([^'"" >]+?)[ '""][^>]*?>";
            MatchCollection matchesImgSrc = Regex.Matches(htmlSource, regexImgSrc, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matchesImgSrc)
            {
                string href = m.Groups[1].Value;
                try
                {
                    links.Add(new Uri(href));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error on loading URI " + ex.Message);
                    LogWriter logObj = new LogWriter(string.Format("Error on loading URI, image: {0}, message: {1}", href, ex.Message));
                }
            }
            return links;
        }


        public static List<Uri> FetchImagesFromLinksSource(string htmlSource)
        {
            List<Uri> links = new List<Uri>();
            string regexImgSrc = @"<a[^>]*href\s*=[""'](?<HRef>[^>]+\.jpg)[ '""]>";
            MatchCollection matchesImgSrc = Regex.Matches(htmlSource, regexImgSrc, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matchesImgSrc)
            {
                string href = m.Groups[1].Value;
                try
                {
                    links.Add(new Uri(href));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка при загрузке URI " + ex.Message  );
                    LogWriter logObj = new LogWriter(string.Format("Ошибка при загрузке URI, image: {0}, message: {1}", href, ex.Message) );

                }
            }
            return links;
        }




    }
}
