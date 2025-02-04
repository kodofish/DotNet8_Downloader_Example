using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ConsoleApp1
{
    internal enum Env
    {
        Stage,
        Prod
    }

    static class Program
    {
        private const string OutputFilePath = "result.json";

        private static readonly Dictionary<Env, string> EnvUrl = new()
        {
            { Env.Stage, $"https://marais-stage.com/line_shopping/product_full?force=true" },
            { Env.Prod, $"https://www.storemarais.com/line_shopping/product_full" }
        };

        static async Task Main(string[] args)
        {
            // 建立計時器
            var stopwatch = Stopwatch.StartNew();
            try
            {
                //檢查檔案是否存在, 存在的話就不呼叫 API
                if (File.Exists(OutputFilePath))
                {
                    Console.WriteLine("檔案已存在，將直接讀取檔案內容...");
                    // 讀取檔案內容
                    await ValidateJsonDescriptionsAsync();
                    Console.WriteLine("處理結束");
                    return;
                }

                Console.WriteLine("開始呼叫 API...");

                var response = await CallApiAndGetResponseAsync(EnvUrl[Env.Stage]);

                // 確認回應是否成功
                response.EnsureSuccessStatusCode();

                Console.WriteLine("取得資料成功，正在寫入檔案...");

                await WriteStreamToFileAsync(response, OutputFilePath);

                Console.WriteLine("檔案寫入完成，開始計算資料筆數...");

                // 計算檔案內的資料筆數
                var recordCount = await CountJsonRecordsAsync(OutputFilePath);

                Console.WriteLine($"檔案內資料筆數：{recordCount}");

                await ValidateJsonDescriptionsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"發生錯誤：{ex.Message}");
            }

            stopwatch.Stop();
            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"共花費時間：{elapsedMilliseconds} 毫秒");
        }

        private static async Task ValidateJsonDescriptionsAsync()
        {
            // 打開檔案作為流
            await using var stream = new FileStream(OutputFilePath, FileMode.Open, FileAccess.Read);
            using var jsonDocument = await JsonDocument.ParseAsync(stream);

            var descriptionTooLong = new List<string>();
            var ldescriptionTooLong = new List<string>();

            // 確認 JSON 根物件為陣列
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var jsonObject in jsonDocument.RootElement.EnumerateArray())
                {
                    jsonObject.TryGetProperty("product_id", out var productId);
                    jsonObject.TryGetProperty("product_name", out var productName);

                    if (jsonObject.TryGetProperty("l_description", out var l_description) &&
                        l_description.ToString().Length > 60000)
                    {
                        ldescriptionTooLong.Add(productId.GetString() + ";" + productName.GetString());
                        continue;
                        // Console.WriteLine(jsonObject.ToString().Substring(0, 60000));
                        // return;
                        var filteredResult = Filter(l_description);
                        if (filteredResult.ToString().Length > 60000)
                        {
                            Console.WriteLine(productId.GetString() + ": l_Description 過濾後長度仍大於 60000");
                            // Console.WriteLine(filteredResult.Substring(0, 60000));
                        }

                        return;
                    }
                }
            }
            else
            {
                Console.WriteLine("非預期的 JSON 格式，根物件不是陣列！");
            }

            ldescriptionTooLong.ForEach(Console.WriteLine);
            Console.WriteLine("l_Description too long: " + ldescriptionTooLong.Count);
        }

        private static string Filter(JsonElement jsonElement)
        {
            const string pattern = @"<img[^>]*src=[""']data:image[^""']*[""'][^>]*>";

            if (jsonElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("lDescription 必须为字符串类型");
            }

            var value = jsonElement.GetString();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // 使用正则移除匹配的部分
            return Regex.Replace(value, pattern, string.Empty);
        }

        private static async Task FetchDataFromFileAsync()
        {
            // 打開檔案作為流
            await using var stream = new FileStream(OutputFilePath, FileMode.Open, FileAccess.Read);
            using var jsonDocument = await JsonDocument.ParseAsync(stream);

            // 確認 JSON 根物件為陣列
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var jsonObject in jsonDocument.RootElement.EnumerateArray())
                {
                    if (jsonObject.TryGetProperty("product_id", out var productId) &&
                        productId.GetString() == "SAO10019")
                    {
                        Console.WriteLine("找到 product_id 為 'SAO10019' 的產品：");
                        Console.WriteLine(jsonObject.ToString());
                        return;
                    }
                }

                Console.WriteLine("找不到 product_id 為 'SAO10019' 的產品！");
            }
            else
            {
                Console.WriteLine("非預期的 JSON 格式，根物件不是陣列！");
            }
        }

        /// <summary>
        /// Calls an API and retrieves the HTTP response asynchronously.
        /// </summary>
        /// <param name="url">The URL of the API to call.</param>
        /// <returns>The HTTP response message from the API call.</returns>
        private static async Task<HttpResponseMessage> CallApiAndGetResponseAsync(string url)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(600);

            return await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }

        /// <summary>
        /// Writes the content of an HTTP response stream to a file asynchronously.
        /// </summary>
        /// <param name="response">The HTTP response containing the stream to be written to the file.</param>
        /// <param name="outputFilePath">The path of the file to write the stream content to.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        private static async Task WriteStreamToFileAsync(HttpResponseMessage response, string outputFilePath)
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write,
                FileShare.None, 8192, true);
            await contentStream.CopyToAsync(fileStream);
        }

        /// <summary>
        /// 使用流式讀取的方式計算 JSON 中的資料筆數
        /// </summary>
        /// <param name="filePath">要解析的檔案路徑</param>
        /// <returns>資料筆數</returns>
        private static async Task<int> CountJsonRecordsAsync(string filePath)
        {
            var count = 0;

            // 使用 FileStream 與 JsonDocument 逐步解析 JSON
            await using var fileStream =
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            using var jsonDocument = await JsonDocument.ParseAsync(fileStream,
                new JsonDocumentOptions { AllowTrailingCommas = true });
            // 確認 JSON 根物件為陣列
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
            {
                count += jsonDocument.RootElement.EnumerateArray().Count();
            }
            else
            {
                Console.WriteLine("非預期的 JSON 格式，根物件不是陣列！");
                return count;
            }

            return count;
        }
    }
}