using Microsoft.Graph;
using StringHelper = CPS_API.Helpers.StringHelper;

namespace CPS_API.Services
{
    public interface IEmailService
    {
        Task GetAuthorEmailAndSendMailAsync(string subject, string content, ListItem listItem);
    }

    public class EmailService : IEmailService
    {
        public async Task GetAuthorEmailAndSendMailAsync(string subject, string content, ListItem listItem)
        {
            var email = GetAuthorEmail(listItem.CreatedBy);
            await SendMailAsync(subject, content, email);
        }

        private string GetAuthorEmail(IdentitySet identity)
        {
            if (identity == null || identity.User == null)
            {
                return "";
            }
            return StringHelper.GetStringValueOrDefault(identity.User.AdditionalData, "email");
        }

        private async Task SendMailAsync(string subject, string content, string email)
        {
            var message = new Message
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
