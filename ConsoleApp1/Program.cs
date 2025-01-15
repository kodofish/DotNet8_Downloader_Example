using System.Diagnostics;
using System.Text.Json;

namespace ConsoleApp1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 要呼叫的 URL
            const string url = $"https://marais-stage.com/line_shopping/product_full";
            //const string url = $"https://www.storemarais.com/line_shopping/product_full";

            // 儲存結果的檔案名稱
            const string outputFilePath = "result.json";

            // 建立 HttpClient
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(600);

            // 建立計時器
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Console.WriteLine("開始呼叫 API...");

                var response = await CallApiAndGetResponseAsync(client, url);

                // 確認回應是否成功
                response.EnsureSuccessStatusCode();

                Console.WriteLine("取得資料成功，正在寫入檔案...");

                await WriteStreamToFileAsync(response, outputFilePath);

                Console.WriteLine("檔案寫入完成，開始計算資料筆數...");

                // 計算檔案內的資料筆數
                var recordCount = await CountJsonRecordsAsync(outputFilePath);

                Console.WriteLine($"檔案內資料筆數：{recordCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"發生錯誤：{ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

                Console.WriteLine($"共花費時間：{elapsedMilliseconds} 毫秒");
            }
        }

        /// <summary>
        /// Calls an API and retrieves the HTTP response asynchronously.
        /// </summary>
        /// <param name="client">The HttpClient instance used to make the API request.</param>
        /// <param name="url">The URL of the API to call.</param>
        /// <returns>The HTTP response message from the API call.</returns>
        private static async Task<HttpResponseMessage> CallApiAndGetResponseAsync(HttpClient client, string url)
        {
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