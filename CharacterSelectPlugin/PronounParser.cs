using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CharacterSelectPlugin
{
    public class PronounSet
    {
        public string Subject { get; set; } = "they"; // they/she/he
        public string Object { get; set; } = "them";  // them/her/him
        public string Possessive { get; set; } = "their"; // their/her/his
        public string PossessivePronoun { get; set; } = "theirs"; // theirs/hers/his
        public string Reflexive { get; set; } = "themselves";  // themselves/herself/himself
        public bool IsPlural { get; set; } = true;  // for verb conjugation

        // Verb forms for this pronoun set
        public string BeVerb
        {
            get { return IsPlural ? "are" : "is"; }
        }

        public string HaveVerb
        {
            get { return IsPlural ? "have" : "has"; }
        }

        public string WereVerb
        {
            get { return IsPlural ? "were" : "was"; }
        }
    }

    public static class PronounParser
    {
        public static PronounSet Parse(string? pronounString)
        {
            if (string.IsNullOrWhiteSpace(pronounString))
                return new PronounSet
                {
                    Subject = "they",
                    Object = "them",
                    Possessive = "their",
                    PossessivePronoun = "theirs",
                    Reflexive = "themselves",
                    IsPlural = true
                };

            string input = pronounString.Trim().ToLower();

            if (input.Contains("/"))
            {
                string[] parts = input.Split('/');
                if (parts.Length >= 2)
                {
                    string subject = parts[0].Trim();
                    string objectPronoun = parts[1].Trim();
                    return CreatePronounSet(subject, objectPronoun);
                }
            }

            return CreatePronounSet(input, null);
        }

        private static PronounSet CreatePronounSet(string subject, string objectHint)
        {
            PronounSet pronounSet = new PronounSet();

            switch (subject.ToLower())
            {
                case "she":
                    pronounSet.Subject = "she";
                    pronounSet.Object = "her";
                    pronounSet.Possessive = "her";
                    pronounSet.PossessivePronoun = "hers";
                    pronounSet.Reflexive = "herself";
                    pronounSet.IsPlural = false;
                    break;

                case "he":
                    pronounSet.Subject = "he";
                    pronounSet.Object = "him";
                    pronounSet.Possessive = "his";
                    pronounSet.PossessivePronoun = "his";
                    pronounSet.Reflexive = "himself";
                    pronounSet.IsPlural = false;
                    break;

                case "they":
                default:
                    pronounSet.Subject = "they";
                    pronounSet.Object = "them";
                    pronounSet.Possessive = "their";
                    pronounSet.PossessivePronoun = "theirs";
                    pronounSet.Reflexive = "themselves";
                    pronounSet.IsPlural = true;
                    break;
            }

            return pronounSet;
        }
    }
}
