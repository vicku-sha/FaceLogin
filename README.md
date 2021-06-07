# FaceLoginPC
 Biometric authentication by the user's face using a single camera (on a PC or laptop).
 
 ***This software is a prototype and can be implemented in various systems: ACS, smartphones, PCs, etc.

The work of the program is divided into four main stages.
1) Face detection, capture, and matching. The Hog method (histogram method) is used to detect faces on the video stream, capture faces, and match faces from the database of faces.

2) Placement of markers on the area of reading facial features. The pre-trained model shape_predictor_68_face_landmarks. dat is used (consisting of 68 points for marking the face: lips, nose, eyes, etc.), the placement of marker points is performed by the Hog method.

3) Determining the position of a person's face. A neural network is used to determine the position of the head. Using the RLS – recursive least squares algorithm, the neural network, drawing vectors by points, learns to calculate the Roll, Pitch, and Yaw coefficients.

4) Data collection and processing by the resulting neural network. Collecting all the coefficients in a data.csv file, using the standard Microsoft algorithm-FastTreeOva and training it using the Visual Studio 2019 development environment (Community version), processing the image on the input stream from the webcam and output the result (whether it is a person or a photo).

***Pay attention to the licenses!!!

Library for identifying and marking faces-the Hog method (you can use the neural network method-cnn) in C#: https://github.com/takuya-takeuchi/FaceRecognitionDotNet

Point marking model (for Hog): https://github.com/davisking/dlib-models

Neural network for determining the position of a person and a guide for its training: https://github.com/takuya-takeuchi/FaceRecognitionDotNet/tree/master/tools/HeadPoseTraining
