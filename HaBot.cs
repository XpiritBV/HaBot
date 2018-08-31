using Microsoft.Bot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Core.Extensions;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace HaBot
{
    public class HaBot : IBot
    {
        private readonly IConfiguration _configuration;
        
        public HaBot(IConfiguration configuration)
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
}
