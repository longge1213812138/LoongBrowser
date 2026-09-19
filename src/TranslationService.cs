// TranslationService.cs —— 百度翻译 API 集成
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace LoongBrowser
{
    /// <summary>
    /// 百度翻译 API 服务
    /// 文档: https://fanyi-api.baidu.com/doc/21
    /// </summary>
    public class TranslationService
    {
        // 百度翻译 API 配置（需要用户自行申请）
        // 免费版: https://fanyi-api.baidu.com/product/11
        private static string _appId = "";
        private static string _secretKey = "";
        private static string _configPath = null;

        /// <summary>翻译结果</summary>
        public class TranslationResult
        {
            public bool Success;
            public string TranslatedText;
            public string ErrorMessage;
            public string SourceLang;
            public string TargetLang;
        }

        /// <summary>配置文件路径</summary>
        public static string ConfigPath
        {
            get
            {
                if (_configPath == null)
                    _configPath = Path.Combine(AppPaths.UserDataDir, "translation_config.json");
                return _configPath;
            }
        }

        /// <summary>检查是否已配置 API 密钥</summary>
        public static bool IsConfigured
        {
            get { return !string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_secretKey); }
        }

        /// <summary>获取当前 App ID（脱敏显示）</summary>
        public static string MaskedAppId
        {
            get
            {
                if (string.IsNullOrEmpty(_appId)) return "";
                if (_appId.Length <= 6) return "***";
                return _appId.Substring(0, 3) + "***" + _appId.Substring(_appId.Length - 3);
            }
        }

        /// <summary>保存配置</summary>
        public static void SaveConfig(string appId, string secretKey)
        {
            _appId = appId ?? "";
            _secretKey = secretKey ?? "";
            try
            {
                var config = new { appId = _appId, secretKey = _secretKey };
                string json = new JavaScriptSerializer().Serialize(config);
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        /// <summary>加载配置</summary>
        public static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var obj = new JavaScriptSerializer().Deserialize<dynamic>(json);
                if (obj != null)
                {
                    _appId = obj.ContainsKey("appId") ? obj["appId"] ?? "" : "";
                    _secretKey = obj.ContainsKey("secretKey") ? obj["secretKey"] ?? "" : "";
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 翻译文本
        /// </summary>
        /// <param name="text">要翻译的文本</param>
        /// <param name="from">源语言（auto=自动检测）</param>
        /// <param name="to">目标语言</param>
        /// <returns>翻译结果</returns>
        public static TranslationResult Translate(string text, string from = "auto", string to = "zh")
        {
            var result = new TranslationResult();
            result.SourceLang = from;
            result.TargetLang = to;

            if (!IsConfigured)
            {
                result.ErrorMessage = "请先配置百度翻译 API 密钥";
                return result;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                result.ErrorMessage = "翻译内容不能为空";
                return result;
            }

            try
            {
                // 百度翻译 API 参数
                string salt = DateTime.Now.Millisecond.ToString();
                string sign = GenerateSign(text, salt);

                // 构建请求 URL
                string url = string.Format(
                    "https://fanyi-api.baidu.com/api/trans/vip/translate?q={0}&from={1}&to={2}&appid={3}&salt={4}&sign={5}",
                    Uri.EscapeDataString(text), from, to, _appId, salt, sign);

                // 发送请求
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 10000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();
                    var json = new JavaScriptSerializer().Deserialize<dynamic>(responseText);

                    if (json.ContainsKey("error_code"))
                    {
                        string errorCode = json["error_code"].ToString();
                        string errorMsg = json.ContainsKey("error_msg") ? json["error_msg"] : "未知错误";
                        result.ErrorMessage = GetErrorMessage(errorCode, errorMsg);
                        return result;
                    }

                    if (json.ContainsKey("trans_result"))
                    {
                        var transResult = json["trans_result"] as System.Collections.ArrayList;
                        if (transResult != null && transResult.Count > 0)
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (var item in transResult)
                            {
                                var dict = item as System.Collections.Generic.Dictionary<string, object>;
                                if (dict != null && dict.ContainsKey("dst"))
                                {
                                    if (sb.Length > 0) sb.AppendLine();
                                    sb.Append(dict["dst"]);
                                }
                            }
                            result.TranslatedText = sb.ToString();
                            result.Success = true;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                result.ErrorMessage = "网络请求失败: " + ex.Message;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "翻译失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 生成签名
        /// 签名规则: md5(appid+q+salt+密钥)
        /// </summary>
        private static string GenerateSign(string text, string salt)
        {
            string str = _appId + text + salt + _secretKey;
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(str);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>获取友好的错误信息</summary>
        private static string GetErrorMessage(string errorCode, string errorMsg)
        {
            switch (errorCode)
            {
                case "54000": return "必填参数为空";
                case "54001": return "签名错误，请检查 App ID 和密钥";
                case "54003": return "访问频率受限，请降低调用频率";
                case "54004": return "账户余额不足";
                case "54005": return "长 query 请求频繁，请降低频率";
                case "58000": return "客户端 IP 非法";
                case "58001": return "译文语言方向不支持";
                case "58002": return "服务已关闭";
                default: return "错误 " + errorCode + ": " + errorMsg;
            }
        }

        /// <summary>语言代码映射</summary>
        public static string GetLangName(string code)
        {
            switch (code)
            {
                case "zh": return "中文";
                case "en": return "英语";
                case "ja": return "日语";
                case "ko": return "韩语";
                case "fr": return "法语";
                case "de": return "德语";
                case "ru": return "俄语";
                case "es": return "西班牙语";
                case "pt": return "葡萄牙语";
                case "it": return "意大利语";
                case "vi": return "越南语";
                case "th": return "泰语";
                case "auto": return "自动检测";
                default: return code;
            }
        }
    }
}
