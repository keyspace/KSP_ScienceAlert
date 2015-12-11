﻿using System;

namespace ScienceAlert.Rules
{
    public class CompositeRuleEmptyException : Exception
    {
        public CompositeRuleEmptyException() : base("Composite rule ConfigNode contains no entries")
        {
            
        }

        public CompositeRuleEmptyException(string message, Exception inner) : base(message, inner)
        {
            
        }
    }
}