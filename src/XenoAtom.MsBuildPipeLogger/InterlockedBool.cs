// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

internal class InterlockedBool
{
    private volatile int _set;

    public InterlockedBool()
    {
        _set = 0;
    }

    public InterlockedBool(bool initialState)
    {
        _set = initialState ? 1 : 0;
    }

    // Returns the previous switch state of the switch
    public bool Set()
    {
#pragma warning disable 420
        return Interlocked.Exchange(ref _set, 1) != 0;
#pragma warning restore 420
    }

    // Returns the previous switch state of the switch
    public bool Unset()
    {
#pragma warning disable 420
        return Interlocked.Exchange(ref _set, 0) != 0;
#pragma warning restore 420
    }

    // Returns the current state
    public static implicit operator bool(InterlockedBool interlockedBool)
    {
        if (interlockedBool is null)
        {
            throw new ArgumentNullException(nameof(interlockedBool));
        }

        return interlockedBool._set != 0;
    }
}