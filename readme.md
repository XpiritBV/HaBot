# HaBot hosted in ASP.NET Core
This starter shows how to integrate a Bot bot with Azure Speaker Recognition.


## Features

It allows you to:
- Manage speaker profiles
  - View
  - Create
  - Enroll (train the recognition model)
  - Delete
- Recognize enrolled speakers in an audio fragment

## Get started

- Sign up for Speaker Recognition here: https://azure.microsoft.com/en-us/services/cognitive-services/speaker-recognition/
- Get the access key from the azure portal
- Put the access key under key `SubscriptionKey` in configuration. (e.g. appsettings.json)


## Sample audio

The sample comes with sample wav files, taken from this talk here:
https://www.youtube.com/watch?v=AJcuX7QvIgM


You can use the files named `loek1.wav`+ `loek2.wav` & `alex1.wav` + `alex2.wav` to enroll two profiles.
After that, use the file named both.wav to analyze. 
The model should recognize loek in the first half, and alex in the second half of the fragment.

## Participate

Feel free to use this code in any way you see fit.
Pull requests are welcome.

Code from Microsoft falls under their (MIT) license.
https://github.com/Microsoft/Cognitive-SpeakerRecognition-Windows/blob/master/LICENSE.md

