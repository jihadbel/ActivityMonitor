`synthetic.mp4` is a 64×64 blue canvas with a moving white square, generated locally for these tests with Chrome MediaRecorder (VP9 in MP4). It contains no desktop, camera, microphone, user profile, or downloaded content. The checked-in fixture is self-contained; Chrome is not required to run tests.

The fixture validates Media Foundation source-reader interop, duration detection and crash recovery. It does not replace a hardware H.264 screen-recording integration test. The optional generator is `scratch/create_synthetic_video.cjs`.
