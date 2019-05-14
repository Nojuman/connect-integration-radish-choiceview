﻿using System;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Refit;

namespace ChoiceViewAPI
{
    public class SmsWorkflow : TwilioWorkflow
    {
        public SmsWorkflow(TwilioApi twilioApi) : base(twilioApi) { }

        public override async Task<JObject> Process(JObject connectEvent, ILambdaContext context)
        {
            var fromNumber = Environment.GetEnvironmentVariable("TWILIO_PHONENUMBER");

            var customerNumber =
                (string)connectEvent.SelectToken("Details.ContactData.CustomerEndpoint.Address");
            var customerNumberType = (string)connectEvent.SelectToken("Details.ContactData.CustomerEndpoint.Type");
            var systemNumber = string.IsNullOrWhiteSpace(fromNumber) ?
                (string)connectEvent.SelectToken("Details.ContactData.SystemEndpoint.Address") : fromNumber;
            var message = (string)connectEvent.SelectToken("Details.Parameters.SmsMessage");
            var systemNumberType = (string)connectEvent.SelectToken("Details.ContactData.SystemEndpoint.Type");
            
            bool result;
            if (customerNumberType.Equals("TELEPHONE_NUMBER") &&
                (!string.IsNullOrWhiteSpace(fromNumber) || systemNumberType.Equals("TELEPHONE_NUMBER")))
            {
                var numberType = await GetPhoneNumberType(customerNumber, context.Logger);
                if (numberType.Equals("mobile"))
                {
                    result = await SendSMS(systemNumber, customerNumber, message, context.Logger);
                }
                else
                {
                    context.Logger.LogLine("Cannot send SMS to a landline or VOIP telephone number.");
                    result = false;
                }
            }
            else
            {
                var errorMessage = !customerNumberType.Equals("TELEPHONE_NUMBER")
                    ? "Customer address"
                    : "System address";
                context.Logger.LogLine($"Cannot send SMS: {errorMessage} is not a valid telephone number.");
                result = false;
            }

            return new JObject(new JProperty("LambdaResult", result));
        }

        private async Task<bool> SendSMS(string fromNumber, string toNumber, string message, ILambdaLogger logger)
        {
            bool result;
            try
            {
                dynamic smsResource = await _twilioApi.MessagingApi.SendSMS(_twilioApi.AccountSid,
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        {"From", fromNumber},
                        {"To", toNumber},
                        {"Body", message + IVRWorkflow.SwitchCallerId(toNumber)}
                    });
                string status = smsResource.status;
                result = status.Equals("queued");
                if (result)
                {
                    logger.LogLine("SMS message queued for sending to " + toNumber);
                    logger.LogLine("Twilio SMS message resource: " + JsonConvert.SerializeObject(smsResource));
                }
                else
                {
                    logger.LogLine("SMS message not queued");
                }
            }
            catch (ApiException ex)
            {
                logger.LogLine("Cannot send SMS: " + ex.Message);
                result = false;
            }
            return result;
        }
    }
}