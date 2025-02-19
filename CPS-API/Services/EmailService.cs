﻿using CPS_API.Models.Exceptions;
using Microsoft.Graph.Models;
using StringHelper = CPS_API.Helpers.StringHelper;

namespace CPS_API.Services
{
    public interface IEmailService
    {
        void GetAuthorEmailAndSendMailAsync(string subject, string content, ListItem listItem);
    }

    public class EmailService : IEmailService
    {
        public void GetAuthorEmailAndSendMailAsync(string subject, string content, ListItem listItem)
        {
            var email = GetAuthorEmail(listItem.CreatedBy);
            if (string.IsNullOrEmpty(email)) throw new CpsException("No email found to send mail");
            SendMailAsync(subject, content, email!);
        }

        private static string? GetAuthorEmail(IdentitySet? identity)
        {
            if (identity == null || identity.User == null)
            {
                return "";
            }
            return StringHelper.GetStringValueOrDefault(identity.User.AdditionalData, "email");
        }

        private static void SendMailAsync(string subject, string content, string email)
        {
            var message = new Message//NOSONAR
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = content,
                },
                ToRecipients = new List<Recipient>
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = email,
                        },
                    },
                }
            };

            // Not implemented yet: Send Mail
        }
    }
}