using Microsoft.Bot;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AttachmentPrompt = Microsoft.Bot.Builder.Dialogs.AttachmentPrompt;
using ChoicePrompt = Microsoft.Bot.Builder.Dialogs.ChoicePrompt;
using TextPrompt = Microsoft.Bot.Builder.Dialogs.TextPrompt;

namespace HaBot
{
    public class EchoBot : IBot
    {
        private readonly IConfiguration _configuration;
        //private readonly DialogSet _dialogs;
        //private readonly RecognizerDialogSet _choiceDialogSet;



        //private async Task AgeValidator(ITurnContext context, NumberResult<int> result)
        //{
        //    if (0 > result.Value || result.Value > 122)
        //    {
        //        result.Status = PromptStatus.NotRecognized;
        //        await context.SendActivity("Your age should be between 0 and 122.");
        //    }
        //}

        //private async Task AskNameStep(DialogContext dialogContext, object result, SkipStepFunction next)
        //{
        //    await dialogContext.Prompt(PromptStep.NamePrompt, "What is your name?");
        //}

        //private async Task AskAgeStep(DialogContext dialogContext, object result, SkipStepFunction next)
        //{
        //    var state = dialogContext.Context.GetConversationState<MultiplePromptsState>();
        //    state.Name = (result as TextResult).Value;
        //    await dialogContext.Prompt(PromptStep.AgePrompt, "What is your age?");
        //}

        //private async Task GatherInfoStep(DialogContext dialogContext, object result, SkipStepFunction next)
        //{
        //    var state = dialogContext.Context.GetConversationState<MultiplePromptsState>();
        //    state.Age = (result as NumberResult<int>).Value;
        //    await dialogContext.Context.SendActivity($"Your name is {state.Name} and your age is {state.Age}");
        //    await dialogContext.End();
        //}

        public EchoBot(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            //_dialogs = new DialogSet();

            // Create prompt for name with string length validation
            //_dialogs.Add(PromptStep.NamePrompt,
            //    new TextPrompt(NameValidator));
            // Create prompt for age with number value validation
            //_dialogs.Add(PromptStep.AgePrompt,
            //    new Microsoft.Bot.Builder.Dialogs.NumberPrompt<int>(Culture.English, AgeValidator));
            // Add a dialog that uses both prompts to gather information from the user
            //_dialogs.Add(PromptStep.GatherInfo,
            //    new WaterfallStep[] { AskNameStep, AskAgeStep, GatherInfoStep });
            //_choiceDialogSet = new RecognizerDialogSet();
        }

        public async Task OnTurn(ITurnContext context)
        {
            var state = context.GetConversationState<ProfileState>();
            DialogContext ctx;

            switch (context.Activity.Type)
            {
                case ActivityTypes.Message:

                    ctx = new MainDialogSet(_configuration).CreateContext(context, state);
                    await ctx.Continue();
                    if (string.IsNullOrWhiteSpace(state.SelectedAction) && !context.Responded)
                    {
                        await ctx.Begin(MainDialogSet.Dialogs.MainDialogName, state);
                    }

                    break;
            }
        }
    }


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
            public const string ViewProfileDialogName = "ViewProfileDialog";
            public const string CreateProfileDialogName = "CreateProfileDialog";
            public const string DeleteProfileDialogName = "DeleteProfileDialog";
            public const string EnrollProfileDialogName = "EnrollProfileDialog";
        }

        //config
        private const string SubscriptionKeySettingKey = "SubscriptionKey";

        //client to talk to Speaker Recognition API
        private readonly SpeakerIdentificationServiceClient _client;

        private static class MainMenu
        {
            public const string ManageProfiles = "Manage Profiles";
            public const string RecognizeSpeaker = "Recognize Speaker";
        }

        private static class ProfileMenu
        {
            public const string ViewProfile = "View Profile";
            public const string DeleteProfile = "Delete Profile";
            public const string CreateProfile = "Create Profile";
            public const string EnrollProfile = "Enroll Profile";
            public const string MainMenu = "Main menu";
        }

        /// <summary>Defines the IDs of the prompts in the set.</summary>
        private static class Inputs
        {
            public const string ManageOrRecognize = "manageOrRecognizePrompt";

            public const string ManageProfile = "managePrompt";

            public const string RecognizeThisPrompt = "recognizeThisPrompt";

            public const string NamePrompt = "namePrompt";

            public const string EnrollProfilePrompt = "enrollProfilePrompt";
        }


        /// <summary>Defines the prompts and steps of the dialog.</summary>
        /// <param name="configuration"></param>
        public MainDialogSet(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            //read settings
            string subscriptionKey = configuration[SubscriptionKeySettingKey];
            if (string.IsNullOrWhiteSpace(subscriptionKey))
                throw new ArgumentException($"{SubscriptionKeySettingKey} setting is missing from configuration",
                    nameof(configuration));

            _client = new SpeakerIdentificationServiceClient(subscriptionKey);

            //add main dialogs
            Add(Inputs.ManageOrRecognize, new ChoicePrompt(Culture.English));

            Add(Dialogs.MainDialogName, new WaterfallStep[]
            {
                async (dc, args, next) =>
                {
                    // Prompt for action.
                    var mainOptions = new List<string>
                    {
                        MainMenu.ManageProfiles,
                        MainMenu.RecognizeSpeaker
                    };
                    await dc.Prompt(Inputs.ManageOrRecognize, "What do you want to do?", new ChoicePromptOptions
                    {
                        Choices = ChoiceFactory.ToChoices(mainOptions),
                        RetryPromptActivity = MessageFactory.SuggestedActions(mainOptions, "Please select an option.") as Activity
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
                    }
                }
            });

            Add(Inputs.NamePrompt, new TextPrompt(NameValidator));


            //add profile dialogs
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
                        ProfileMenu.MainMenu
                    };
                    await dc.Prompt(Inputs.ManageProfile, "What do you want to do?", new ChoicePromptOptions
                    {
                        Choices = ChoiceFactory.ToChoices(mainOptions),
                        RetryPromptActivity = MessageFactory.SuggestedActions(mainOptions, "Please select an option.") as Activity
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

                        case ProfileMenu.MainMenu:
                            await dc.Replace(Dialogs.MainDialogName);
                            break;
                    }
                }});

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
                        await dc.Context.SendActivity($"I know you {state.Name}. Your existing profile id is: {state.ProfileId.Value}");
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

            //add recognizer dialogs
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

                    string attachmentContentUrl = attachment.ContentUrl;

                    //send attachment in chunks to be analyzed
                    await dc.Context.SendActivity("Analyzing your voice...");

                    await AnalyzeWavFile(attachmentContentUrl, dc);

                    await dc.Context.SendActivity("Analysis complete.");
                    //we're done
                    await dc.Replace(Dialogs.MainDialogName);
                }
            });
        }

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
        /// <returns></returns>
        private async Task AnalyzeWavFile(string attachmentContentUrl, DialogContext dc)
        {
            try
            {
                var stream = await _httpClient.GetStreamAsync(attachmentContentUrl);
                var state = dc.Context.GetConversationState<ProfileState>();
                var audioFormat = new AudioFormat(AudioEncoding.PCM, 1, 16000, 16,
                    new AudioContainer(AudioContainerType.WAV));

                async Task ResultCallBack(RecognitionResult partialResult)
                {
                    if (partialResult.Succeeded)
                    {
                        await dc.Context.SendActivity(
                            $"Recognized profile '{partialResult.Value.IdentifiedProfileId}', confidence '{partialResult.Value.Confidence}'.");
                    }
                    else
                    {
                        await dc.Context.SendActivity($"Recognition failed with error '{partialResult.FailureMsg}'.");
                    }
                }

                using (var client = new ClientFactory().CreateRecognitionClient(_configuration, Guid.NewGuid(),
                    state.AllSpeakers, 5, 10, audioFormat, ResultCallBack, _client))
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


        private async Task NameValidator(ITurnContext context, TextResult result)
        {
            if (result.Value.Length <= 2)
            {
                result.Status = PromptStatus.NotRecognized;
                await context.SendActivity("Your name should be at least 2 characters long.");
            }
        }



    }

    /// <summary>
    /// Class for storing conversation state.
    /// </summary>
    public class ProfileState : Dictionary<string, object>
    {
        private const string NameKey = "name";
        private const string ProfileKey = "profile";
        private const string SelectActionKey = "action";
        private const string EnrollmentStatusKey = "enrollmentStatus";

        private static readonly Guid AlexId = new Guid("4ac7dda3-56a5-45cc-8bda-1183899bf4bf");
        private static readonly Guid LoekId = new Guid("ab8d4c0d-2896-47ac-9c79-d7fb0efb1bb3");
        private static readonly Guid UnEnrolledId = new Guid("b000d66e-f846-4a98-865e-e0d34a0480d0");

        public Guid[] AllSpeakers = { LoekId, AlexId };

        public const string ManageProfiles = "Manage Profiles";
        public const string RecognizeSpeaker = "Recognize Speaker";

        public string Name
        {
            get => (string)(TryGetValue(NameKey, out var value) ? value : null);
            set
            {
                this[NameKey] = value;

                //makes up for the fact that we have no centralized storage for state.
                if (value.ToUpperInvariant() == "LOEK")
                    ProfileId = LoekId;
                else if (value.ToUpperInvariant() == "ALEX")
                    ProfileId = AlexId;
                else if (value.ToUpperInvariant() == "STRANGER")
                    ProfileId = UnEnrolledId;
            }
        }

        public Guid? ProfileId
        {
            get
            {
                if (!TryGetValue(ProfileKey, out var value))
                {
                    return null;
                }

                if (value is string s)
                    return new Guid(s);

                var guid = value as Guid?;
                return guid;
            }
            set => this[ProfileKey] = value;
        }

        public string SelectedAction
        {
            get => (string)(TryGetValue(SelectActionKey, out var value) ? value : null);
            set => this[SelectActionKey] = value;
        }

        public EnrollmentStatus? EnrollmentStatus
        {
            get => (EnrollmentStatus?)(int?)(TryGetValue(EnrollmentStatusKey, out var value) ? value : null);
            set => this[EnrollmentStatusKey] = value;
        }
    }
}
