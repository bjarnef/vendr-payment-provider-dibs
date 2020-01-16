using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Vendr.Core;
using Vendr.Core.Models;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.PaymentProviders.Dibs
{
    [PaymentProvider("dibs-d2", "DIBS D2", "DIBS D2 payment provider for one time payments")]
    public class DibsPaymentProvider : PaymentProviderBase<DibsSettings>
    {
        

        public DibsPaymentProvider(VendrContext vendr)
            : base(vendr)
        { }

        public override bool CanCancelPayments => true;
        public override bool CanCapturePayments => true;
        public override bool CanRefundPayments => true;
        public override bool CanFetchPaymentStatus => true;

        public override bool FinalizeAtContinueUrl => false;

        public override string GetCancelUrl(OrderReadOnly order, DibsSettings settings)
        {
            settings.MustNotBeNull("settings");
            settings.CancelUrl.MustNotBeNull("settings.CancelUrl");

            return settings.CancelUrl;
        }

        public override string GetErrorUrl(OrderReadOnly order, DibsSettings settings)
        {
            settings.MustNotBeNull("settings");
            settings.ErrorUrl.MustNotBeNull("settings.ErrorUrl");

            return settings.ErrorUrl;
        }

        public override string GetContinueUrl(OrderReadOnly order, DibsSettings settings)
        {
            settings.MustNotBeNull("settings");
            settings.ContinueUrl.MustNotBeNull("settings.ContinueUrl");

            return settings.ContinueUrl;
        }

        public override PaymentForm GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, DibsSettings settings)
        {
            var currency = Vendr.Services.CurrencyService.GetCurrency(order.CurrencyId);

            // Ensure currency has valid ISO 4217 code
            if (!ISO4217.Codes.ContainsKey(currency.Code.ToUpperInvariant())) {
                throw new Exception("Currency must a valid ISO 4217 currency code: " + currency.Name);
            }

            var currencyStr = ISO4217.Codes[currency.Code.ToUpperInvariant()].ToString(CultureInfo.InvariantCulture);
            var orderAmount = (order.TotalPrice.Value.WithTax * 100M).ToString("0", CultureInfo.InvariantCulture);

            // MD5(key2 + MD5(key1 + "merchant=<merchant>&orderid=<orderid> &currency=<cur>&amount=<amount>"))
            var md5Check = $"merchant={settings.MerchantId}&orderid={order.OrderNumber}&currency={currencyStr}&amount={orderAmount}";
            var md5Hash = GetMD5Hash(settings.MD5Key2 + GetMD5Hash(settings.MD5Key1 + md5Check));

            return new PaymentForm("https://payment.architrade.com/paymentweb/start.action", FormMethod.Post)
                .WithInput("orderid", order.OrderNumber)
                .WithInput("merchant", settings.MerchantId)
                .WithInput("amount", orderAmount)
                .WithInput("currency", currencyStr)
                .WithInput("accepturl", continueUrl)
                .WithInput("cancelurl", cancelUrl)
                .WithInput("callbackurl", callbackUrl)
                .WithInputIf("capturenow", settings.Capture, "yes")
                .WithInputIf("calcfee", settings.CalcFee, "yes")
                .WithInputIf("test", settings.Mode == DibsMode.Test, "yes")
                .WithInput("md5key", md5Hash);
        }

        public override CallbackResponse ProcessCallback(OrderReadOnly order, HttpRequestBase request, DibsSettings settings)
        {
            return new CallbackResponse
            {
                TransactionInfo = new TransactionInfo
                {
                    AmountAuthorized = order.TotalPrice.Value.WithTax,
                    TransactionFee = 0m,
                    TransactionId = Guid.NewGuid().ToString("N"),
                    PaymentStatus = PaymentStatus.Authorized
                }
            };
        }

        public override ApiResponse CancelPayment(OrderReadOnly order, DibsSettings settings)
        {
            return new ApiResponse(order.TransactionInfo.TransactionId, PaymentStatus.Cancelled);
        }

        public override ApiResponse CapturePayment(OrderReadOnly order, DibsSettings settings)
        {
            return new ApiResponse(order.TransactionInfo.TransactionId, PaymentStatus.Captured);
        }

        private static string GetMD5Hash(string input)
        {
            var hash = new StringBuilder();

            using (var md5provider = new MD5CryptoServiceProvider())
            {
                var bytes = md5provider.ComputeHash(new UTF8Encoding().GetBytes(input));

                for (var i = 0; i < bytes.Length; i++)
                {
                    hash.Append(bytes[i].ToString("x2"));
                }
            }

            return hash.ToString();
        }
    }
}