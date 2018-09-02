using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Core.Extensions;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Prompts;
using Microsoft.Bot.Builder.Prompts.Choices;
using Microsoft.Bot.Schema;
using Microsoft.Cognitive.SpeakerRecognition.Streaming.Audio;
using Microsoft.Cognitive.SpeakerRecognition.Streaming.Client;
using Microsoft.Cognitive.SpeakerRecognition.Streaming.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.ProjectOxford.SpeakerRecognition;
using Microsoft.ProjectOxford.SpeakerRecognition.Contract;
using Microsoft.ProjectOxford.SpeakerRecognition.Contract.Identification;
using Microsoft.Recognizers.Text;
using AttachmentPrompt = Microsoft.Bot.Builder.Dialogs.AttachmentPrompt;
using ChoicePrompt = Microsoft.Bot.Builder.Dialogs.ChoicePrompt;
using TextPrompt = Microsoft.Bot.Builder.Dialogs.TextPrompt;

namespace HaBot
{
    /// <summary>Defines a dialog recognizes a user.</summary>
    public class MainDialogSet : DialogSet
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient = new HttpClient();

        internal static class Dialogs
        {
            public const string MainDialogName = "MainDialog";
            public const string ManageProfileDialogName = "ManageProfileDialog";
            public const string RecognizeSpeakerDialogName = "RecognizeSpeakerDialog";
            public const string SpeechToTextDialogName = "SpeechToTextDialog";
            public const string AnalyzeTextDialogName = "AnalyzeTextDialog";
            public const string ViewProfileDialogName = "ViewProfileDialog";
            public const string CreateProfileDialogName = "CreateProfileDialog";
            public const string DeleteProfileDialogName = "DeleteProfileDialog";
            public const string EnrollProfileDialogName = "EnrollProfileDialog";
        }

        //config
        private const string SubscriptionKeySettingKey = "SubscriptionKey";

        //client to talk to Speaker Recognition API
        private readonly SpeakerIdentificationServiceClient _client;

        /// <summary>
        /// Main menu items
        /// </summary>
        private static class MainMenu
        {
            public const string ManageProfiles = "Manage Profiles";
            public const string RecognizeSpeaker = "Recognize Speaker";
            public const string SpeechToText = "Speech to Text";
            public const string AnalyzeText = "Analyze text";
        }

        /// <summary>
        /// Profile menu items
        /// </summary>
        private static class ProfileMenu
        {
            public const string ViewProfile = "View Profile";
            public const string DeleteProfile = "Delete Profile";
            public const string CreateProfile = "Create Profile";
            public const string EnrollProfile = "Enroll Profile";
            public const string BackToMainMenu = "Main menu";
        }

        /// <summary>
        /// Defines the IDs of the input prompts.
        /// </summary>
        private static class Inputs
        {
            public const string ManageOrRecognize = "manageOrRecognizePrompt";

            public const string ManageProfile = "managePrompt";

            public const string RecognizeThisPrompt = "recognizeThisPrompt";

            public const string RecognizeThisTTSPrompt = "recognizeThisTSSPrompt";

            public const string AnalyzeTextPrompt = "analyzeTextPrompt";

            public const string NamePrompt = "namePrompt";

            public const string EnrollProfilePrompt = "enrollProfilePrompt";
        }


        /// <summary>Defines the prompts and steps of the dialog.</summary>
        /// <param name="configuration"></param>
        public MainDialogSet(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            //config settings
            string subscriptionKey = configuration[SubscriptionKeySettingKey];
            if (string.IsNullOrWhiteSpace(subscriptionKey))
                throw new ArgumentException($"{SubscriptionKeySettingKey} setting is missing from configuration",
                    nameof(configuration));

            //communication client for Azure Speaker Recognition
            _client = new SpeakerIdentificationServiceClient(subscriptionKey);

            //add main dialog
            AddMainDialog();

            //add profile dialog
            AddProfileDialog();

            //add recognizer dialog
            AddRecognizerDialog();

            //add speech to text dialog
            AddSpeechToTextDialog();

            //add text sentiment analyzes
            AddTextAnalyzes();
        }


        /// <summary>
        /// Adds speech to text dialog
        /// </summary>
        private void AddTextAnalyzes()
        {
            Add(Inputs.AnalyzeTextPrompt, new TextPrompt(SentenceValidator));
            Add(Dialogs.AnalyzeTextDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    await dc.Prompt(Inputs.AnalyzeTextPrompt, "How is your day going?");
                },
                async (dc, args, next) =>
                {
                   string sentence = (string) args["Value"];
                   var textClient = new ClientFactory().CreateTextAnalyticsClient(_configuration);

                   await dc.Context.SendActivity(await textClient.GetSentimentFromText(sentence));

                   //we're done
                   await dc.Replace(Dialogs.MainDialogName);
                }
            });
        }

        /// <summary>
        /// Adds speech to text dialog
        /// </summary>
        private void AddSpeechToTextDialog()
        {
            Add(Inputs.RecognizeThisTTSPrompt, new AttachmentPrompt());

            Add(Dialogs.SpeechToTextDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    await dc.Prompt(Inputs.RecognizeThisTTSPrompt, "Please upload a .wav file", new PromptOptions());
                },
                async (dc, args, next) =>
                {
                    //Get attachment details
                    var attachment = ((List<Attachment>) args["Attachments"]).FirstOrDefault();

                    if (attachment == null)
                    {
                        await dc.Context.SendActivity("I didn't get the attachment...");
                        //we're done
                        await dc.Replace(Dialogs.MainDialogName);
                        return;
                    }

                    if (attachment.ContentType != "audio/wav" || string.IsNullOrWhiteSpace(attachment.ContentUrl))
                    {
                        await dc.Context.SendActivity($"I didn't get a .wav file attachment...");
                        //we're done
                        await dc.Replace(Dialogs.MainDialogName);
                        return;
                    }

                    var state = dc.Context.GetConversationState<ProfileState>();

                    string attachmentContentUrl = attachment.ContentUrl;

                    //send attachment in chunks to be analyzed
                    await dc.Context.SendActivity("Analyzing text...");

                    await AnalyzeSpeechFile(attachmentContentUrl, dc);
                   
                    await dc.Context.SendActivity("Analysis complete.");
                    //we're done
                    await dc.Replace(Dialogs.MainDialogName);
                }
            });
        }

        /// <summary>
        /// Adds speaker recognition dialog
        /// </summary>
        private void AddRecognizerDialog()
        {
            Add(Inputs.RecognizeThisPrompt, new AttachmentPrompt());

            Add(Dialogs.RecognizeSpeakerDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    await dc.Prompt(Inputs.RecognizeThisPrompt, "Please upload a .wav file", new PromptOptions());
                },
                async (dc, args, next) =>
                {
                    //Get attachment details
                    var attachment = ((List<Attachment>) args["Attachments"]).FirstOrDefault();

                    if (attachment == null)
                    {
                        await dc.Context.SendActivity("I didn't get the attachment...");
                        //we're done
                        await dc.Replace(Dialogs.MainDialogName);
                        return;
                    }

                    if (attachment.ContentType != "audio/wav" || string.IsNullOrWhiteSpace(attachment.ContentUrl))
                    {
                        await dc.Context.SendActivity($"I didn't get a .wav file attachment...");
                        //we're done
                        await dc.Replace(Dialogs.MainDialogName);
                        return;
                    }

                    var state = dc.Context.GetConversationState<ProfileState>();

                    string attachmentContentUrl = attachment.ContentUrl;

                    //Get all enrolled profiles
                    var profiles = await _client.GetProfilesAsync();
                    foreach (var profile in profiles)
                    {
                        if (profile.EnrollmentStatus == EnrollmentStatus.Enrolled
                            && !state.AllSpeakers.Contains(profile.ProfileId))
                        {
                            state.AllSpeakers.Add(profile.ProfileId);
                        }
                    }

                    //send attachment in chunks to be analyzed
                    await dc.Context.SendActivity("Analyzing your voice...");

                    await AnalyzeWavFile(attachmentContentUrl, dc, state);

                    await dc.Context.SendActivity("Analysis complete.");
                    //we're done
                    await dc.Replace(Dialogs.MainDialogName);
                }
            });
        }

        /// <summary>
        /// Adds speaker profile management dialog
        /// </summary>
        private void AddProfileDialog()
        {
            Add(Inputs.ManageProfile, new ChoicePrompt(Culture.English));

            Add(Dialogs.ManageProfileDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    // Prompt for action.
                    var mainOptions = new List<string>
                    {
                        ProfileMenu.ViewProfile,
                        ProfileMenu.CreateProfile,
                        ProfileMenu.DeleteProfile,
                        ProfileMenu.EnrollProfile,
                        ProfileMenu.BackToMainMenu
                    };
                    await dc.Prompt(Inputs.ManageProfile, "What do you want to do?", new ChoicePromptOptions
                    {
                        Choices = ChoiceFactory.ToChoices(mainOptions),
                        RetryPromptActivity =
                            MessageFactory.SuggestedActions(mainOptions, "Please select an option.") as Activity
                    });
                },
                async (dc, args, next) =>
                {
                    var action = (FoundChoice) args["Value"];
                    switch (action.Value)
                    {
                        case ProfileMenu.ViewProfile:
                            await dc.Replace(Dialogs.ViewProfileDialogName);
                            break;

                        case ProfileMenu.CreateProfile:
                            await dc.Replace(Dialogs.CreateProfileDialogName);
                            break;

                        case ProfileMenu.DeleteProfile:
                            await dc.Replace(Dialogs.DeleteProfileDialogName);
                            break;

                        case ProfileMenu.EnrollProfile:
                            await dc.Replace(Dialogs.EnrollProfileDialogName);
                            break;

                        case ProfileMenu.BackToMainMenu:
                            await dc.Replace(Dialogs.MainDialogName);
                            break;
                    }
                }
            });

            Add(Dialogs.ViewProfileDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        await dc.Prompt(Inputs.NamePrompt, "What is your name?");
                    }
                    else
                    {
                        await next(args);
                    }
                },
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        string name = (string) args["Value"];
                        state.Name = name;
                    }

                    if (state.ProfileId.HasValue)
                    {
                        var profile = await _client.GetProfileAsync(state.ProfileId.Value);
                        state.EnrollmentStatus = profile.EnrollmentStatus;
                        switch (profile.EnrollmentStatus)
                        {
                            case EnrollmentStatus.Enrolling:
                                await dc.Context.SendActivity(
                                    $"Welcome back {state.Name}. You are enrolling, {profile.RemainingEnrollmentSpeechSeconds}s remaining");
                                break;
                            case EnrollmentStatus.Training:
                                await dc.Context.SendActivity($"Welcome back {state.Name}. Your profile is being trained.");
                                break;
                            case EnrollmentStatus.Enrolled:
                                await dc.Context.SendActivity($"Welcome back {state.Name}. Your profile is enrolled.");
                                break;
                        }
                    }
                    else
                    {
                        await dc.Context.SendActivity($"I haven't seen you before, {state.Name}");
                    }

                    //we're done
                    await dc.Replace(Dialogs.ManageProfileDialogName);
                }
            });

            Add(Dialogs.CreateProfileDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        await dc.Prompt(Inputs.NamePrompt, "What is your name?");
                    }
                    else
                    {
                        await next(args);
                    }
                },
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        string name = (string) args["Value"];
                        state.Name = name;
                    }

                    if (state.ProfileId.HasValue)
                    {
                        //exists
                        await dc.Context.SendActivity(
                            $"I know you {state.Name}. Your existing profile id is: {state.ProfileId.Value}");
                    }
                    else
                    {
                        //new
                        await dc.Context.SendActivity("Creating a new profile...");
                        var result = await _client.CreateProfileAsync("en-US");
                        state.ProfileId = result.ProfileId;
                        state.EnrollmentStatus = EnrollmentStatus.Enrolling;
                        await dc.Context.SendActivity($"Welcome {state.Name}. Your new profile id is: {result.ProfileId}");

                        //we're done
                        await dc.Replace(Dialogs.ManageProfileDialogName);
                    }
                }
            });

            Add(Dialogs.DeleteProfileDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        await dc.Prompt(Inputs.NamePrompt, "What is your name?");
                    }
                    else
                    {
                        await next(args);
                    }
                },
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        string name = (string) args["Value"];
                        state.Name = name;
                    }

                    if (state.ProfileId.HasValue)
                    {
                        //exists
                        await dc.Context.SendActivity($"Deleting your profile");
                        await _client.DeleteProfileAsync(state.ProfileId.Value);
                        state.ProfileId = null;
                        await dc.Context.SendActivity($"Deleted your profile");
                    }
                    else
                    {
                        //new
                        await dc.Context.SendActivity("I'm sorry, you don't have a profile to delete.");
                    }

                    //we're done
                    await dc.Replace(Dialogs.ManageProfileDialogName);
                }
            });

            Add(Dialogs.EnrollProfileDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        await dc.Prompt(Inputs.NamePrompt, "What is your name?");
                    }
                    else
                    {
                        await next(args);
                    }
                },
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (string.IsNullOrWhiteSpace(state.Name))
                    {
                        string name = (string) args["Value"];
                        state.Name = name;
                    }

                    if (state.ProfileId.HasValue)
                    {
                        //exists
                        await dc.Context.SendActivity($"Enrolling your profile");
                        await dc.Prompt(Inputs.EnrollProfilePrompt, "Please upload a .wav file", new PromptOptions());
                    }
                    else
                    {
                        //new
                        await dc.Context.SendActivity("I'm sorry, you don't have a profile to enroll.");
                        //we're done
                        await dc.Replace(Dialogs.ManageProfileDialogName);
                    }
                },
                async (dc, args, next) =>
                {
                    var state = dc.Context.GetConversationState<ProfileState>();
                    if (!state.ProfileId.HasValue)
                    {
                        //new
                        await dc.Context.SendActivity("I'm sorry, you don't have a profile to enroll.");
                        //we're done
                        await dc.Replace(Dialogs.ManageProfileDialogName);
                        return;
                    }

                    //Get attachment details
                    var attachment = ((List<Attachment>) args["Attachments"]).FirstOrDefault();

                    if (attachment == null)
                    {
                        await dc.Context.SendActivity("I didn't get the attachment...");
                        //we're done
                        await dc.Replace(Dialogs.ManageProfileDialogName);
                        return;
                    }

                    if (attachment.ContentType != "audio/wav" || string.IsNullOrWhiteSpace(attachment.ContentUrl))
                    {
                        await dc.Context.SendActivity($"I didn't get a .wav file attachment...");
                        //we're done
                        await dc.Replace(Dialogs.ManageProfileDialogName);
                        return;
                    }

                    string attachmentContentUrl = attachment.ContentUrl;

                    //send attachment in chunks to be analyzed
                    await dc.Context.SendActivity("Enrolling a profile with your voice...");

                    await EnrollWavFile(attachmentContentUrl, state.ProfileId.Value, dc);

                    await dc.Context.SendActivity("Enrolling of attachment is complete.");

                    //we're done
                    await dc.Replace(Dialogs.ManageProfileDialogName);
                }
            });

            Add(Inputs.EnrollProfilePrompt, new AttachmentPrompt());
        }

        /// <summary>
        /// Adds main menu
        /// </summary>
        private void AddMainDialog()
        {
            Add(Inputs.ManageOrRecognize, new ChoicePrompt(Culture.English));

            Add(Dialogs.MainDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    // Prompt for action.
                    var mainOptions = new List<string>
                    {
                        MainMenu.ManageProfiles,
                        MainMenu.RecognizeSpeaker,
                        MainMenu.SpeechToText,
                        MainMenu.AnalyzeText
                    };
                    await dc.Prompt(Inputs.ManageOrRecognize, "What do you want to do?", new ChoicePromptOptions
                    {
                        Choices = ChoiceFactory.ToChoices(mainOptions),
                        RetryPromptActivity =
                            MessageFactory.SuggestedActions(mainOptions, "Please select an option.") as Activity
                    });
                },
                async (dc, args, next) =>
                {
                    var action = (FoundChoice) args["Value"];
                    switch (action.Value)
                    {
                        case MainMenu.ManageProfiles:
                            await dc.Replace(Dialogs.ManageProfileDialogName);
                            break;

                        case MainMenu.RecognizeSpeaker:
                            await dc.Replace(Dialogs.RecognizeSpeakerDialogName);
                            break;

                        case MainMenu.SpeechToText:
                            await dc.Replace(Dialogs.SpeechToTextDialogName);
                            break;

                        case MainMenu.AnalyzeText:
                            await dc.Replace(Dialogs.AnalyzeTextDialogName);
                            break;
                    }
                }
            });

            Add(Inputs.NamePrompt, new TextPrompt(NameValidator));
        }

        /// <summary>
        /// Takes the uploaded attachment and uses that to enroll the current profile.
        /// </summary>
        /// <param name="attachmentContentUrl"></param>
        /// <param name="profileId"></param>
        /// <param name="dc"></param>
        /// <returns></returns>
        private async Task EnrollWavFile(string attachmentContentUrl, Guid profileId, DialogContext dc)
        {
            try
            {
                var stream = await _httpClient.GetStreamAsync(attachmentContentUrl);

                using (stream)
                {
                    var processPollingLocation = await _client.EnrollAsync(stream, profileId);

                    int numberOfRetries = 10;
                    var timeBetweenRetries = TimeSpan.FromSeconds(5.0);

                    while (numberOfRetries > 0)
                    {
                        await Task.Delay(timeBetweenRetries);
                        var enrollmentResult = await _client.CheckEnrollmentStatusAsync(processPollingLocation);

                        if (enrollmentResult.Status == Status.Succeeded)
                        {
                            break;
                        }

                        if (enrollmentResult.Status == Status.Failed)
                        {
                            throw new EnrollmentException(enrollmentResult.Message);
                        }

                        numberOfRetries--;
                    }

                    if (numberOfRetries <= 0)
                    {
                        throw new EnrollmentException("Enrollment operation timeout.");
                    }
                }

            }
            catch (Exception ex)
            {
                await dc.Context.SendActivity($"Enrollment failed with error '{ex.Message}'.");
            }
        }

        /// <summary>
        /// Analyzes a .wav file to see if there are any known speakers in it.
        /// </summary>
        /// <param name="attachmentContentUrl"></param>
        /// <param name="dc"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private async Task AnalyzeWavFile(string attachmentContentUrl, DialogContext dc, ProfileState state)
        {
            try
            {
                var stream = await _httpClient.GetStreamAsync(attachmentContentUrl);
                var audioFormat = new AudioFormat(AudioEncoding.PCM, 1, 16000, 16,
                    new AudioContainer(AudioContainerType.WAV));

                async Task ResultCallBack(RecognitionResult partialResult)
                {
                    if (partialResult.Succeeded)
                    {
                        var profileId = partialResult.Value.IdentifiedProfileId;

                        if (profileId == state.ProfileId.GetValueOrDefault())
                        {
                            await dc.Context.SendActivity($"Recognized you, confidence '{partialResult.Value.Confidence}'.");
                        }
                        else
                        {
                            await dc.Context.SendActivity($"Recognized other profile '{profileId}', confidence '{partialResult.Value.Confidence}'.");
                        }
                    }
                    else
                    {
                        await dc.Context.SendActivity($"Recognition failed with error '{partialResult.FailureMsg}'.");
                    }
                }

                using (var client = new ClientFactory().CreateRecognitionClient(_configuration, Guid.NewGuid(), state.AllSpeakers.ToArray(), 5, 10, audioFormat, ResultCallBack, _client))
                {
                    using (stream)
                    {
                        var chunkSize = 32000;
                        var buffer = new byte[chunkSize];
                        int bytesRead;

                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            await client.StreamAudioAsync(buffer, 0, bytesRead);
                            await Task.Delay(1000);
                        }

                        await client.EndStreamAudioAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                await dc.Context.SendActivity($"Recognition failed with error '{ex.Message}'.");
            }
        }



        private async Task AnalyzeSpeechFile(string attachmentContentUrl, DialogContext dc)
        {
            var client = new ClientFactory().CreateSTTClient(_configuration);
            var result = await client.TranslateToText(attachmentContentUrl).ConfigureAwait(false);
            await dc.Context.SendActivity(result);
        }

        /// <summary>
        /// Asks for user name.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private async Task SentenceValidator(ITurnContext context, TextResult result)
        {
            if (result.Value.Length <= 4)
            {
                result.Status = PromptStatus.NotRecognized;
                await context.SendActivity("Your sentence should be at least 4 characters long.");
            }
        }

        /// <summary>
        /// Asks for user name.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private async Task NameValidator(ITurnContext context, TextResult result)
        {
            if (result.Value.Length <= 2)
            {
                result.Status = PromptStatus.NotRecognized;
                await context.SendActivity("Your name should be at least 2 characters long.");
            }
        }
    }
}