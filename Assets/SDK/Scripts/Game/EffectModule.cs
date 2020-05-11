﻿using System;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace BS
{
    public class EffectModule
    {
#if ODIN_INSPECTOR
        [BoxGroup("Effect")]
#endif
        public Effect.Step step = Effect.Step.Start;

        public virtual void Refresh()
        {

        }

#if ProjectCore

        public virtual Effect Spawn(EffectData effectData)
        {
            return null;
        }
#endif
    }
}
