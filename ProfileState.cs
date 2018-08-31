using System;
using System.Collections.Generic;
using Microsoft.ProjectOxford.SpeakerRecognition.Contract;

namespace HaBot
{
    /// <summary>
    /// Class for storing conversation state.
    /// </summary>
    public class ProfileState : Dictionary<string, object>
    {
        private const string NameKey = "name";
        private const string ProfileKey = "profile";
        private const string SelectActionKey = "action";
        private const string EnrollmentStatusKey = "enrollmentStatus";

        //These will only work in the author's Azure environment, but shouldn't bother you. Feel free to delete them.
        public static readonly Guid AlexId = new Guid("4ac7dda3-56a5-45cc-8bda-1183899bf4bf");
        public static readonly Guid LoekId = new Guid("ab8d4c0d-2896-47ac-9c79-d7fb0efb1bb3");
        public static readonly Guid UnEnrolledId = new Guid("36ca1410-4460-4271-aeca-9aa4934842f7");

        /// <summary>
        /// Volatile set of all enrolled profiles.
        /// </summary>
        public HashSet<Guid> AllSpeakers { get; } = new HashSet<Guid>();

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
                //issue in state store, types may change
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