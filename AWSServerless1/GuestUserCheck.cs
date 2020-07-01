using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Util;
using System.Diagnostics;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AWSServerless1
{

    public class GuestUserCheck
    {
        IAmazonS3 S3Client { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public GuestUserCheck()
        {
            S3Client = new AmazonS3Client();
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public GuestUserCheck(IAmazonS3 s3Client)
        {
            this.S3Client = s3Client;
        }

        public async Task<String> addFacesToCollectionasync(string photo, string bucket)
        {
            string faceId = null;
            string collectionId = "guest_auth_app_index_faces_from_cam_7DF07FBF-18D0-4087-A7F0-F0E72B380E5D";

            AmazonRekognitionClient rekognitionClient = new AmazonRekognitionClient();

            Image image = new Image()
            {
                S3Object = new S3Object()
                {
                    Bucket = bucket,
                    Name = photo
                }
            };

            IndexFacesRequest indexFacesRequest = new IndexFacesRequest()
            {
                Image = image,
                CollectionId = collectionId,
                MaxFaces = 1,
                ExternalImageId = photo,
                DetectionAttributes = new List<String>() { "ALL" }
            };

            IndexFacesResponse indexFacesResponse = await rekognitionClient.IndexFacesAsync(indexFacesRequest);

            LambdaLogger.Log(photo + " added");
            foreach (FaceRecord faceRecord in indexFacesResponse.FaceRecords)
            {
                faceId = faceRecord.Face.FaceId;
                LambdaLogger.Log("Face detected: Faceid is " +
                   faceRecord.Face.FaceId + "Image id is " + faceRecord.Face.ImageId);
            }

            return faceId;
        }

        public async Task searchFaceByID(string faceId)
        {
            string collectionId = "guest_auth_app_index_faces_from_cam_7DF07FBF-18D0-4087-A7F0-F0E72B380E5D";

            AmazonRekognitionClient rekognitionClient = new AmazonRekognitionClient();

            // Search collection for faces matching the face id.

            SearchFacesRequest searchFacesRequest = new SearchFacesRequest()
            {
                CollectionId = collectionId,
                FaceId = faceId,
                FaceMatchThreshold = 70F,
                MaxFaces = 2
            };

            SearchFacesResponse searchFacesResponse = await rekognitionClient.SearchFacesAsync(searchFacesRequest);

            LambdaLogger.Log("Face matching faceId " + faceId);

            LambdaLogger.Log("Matche(s): ");
            foreach (FaceMatch face in searchFacesResponse.FaceMatches)
                LambdaLogger.Log("FaceId: " + face.Face.FaceId + ", Similarity: " + face.Similarity);
        }

        public async Task<string> searchFaceFromImage(string photo, string bucket)
        {
            string faceId = null;
            string collectionId = "guest_auth_app_index_faces_from_cam_7DF07FBF-18D0-4087-A7F0-F0E72B380E5D";

            AmazonRekognitionClient rekognitionClient = new AmazonRekognitionClient();

            // Get an image object from S3 bucket.
            Image image = new Image()
            {
                S3Object = new S3Object()
                {
                    Bucket = bucket,
                    Name = photo
                }
            };

            SearchFacesByImageRequest searchFacesByImageRequest = new SearchFacesByImageRequest()
            {
                CollectionId = collectionId,
                Image = image,
                FaceMatchThreshold = 70F,
                MaxFaces = 2
            };

            SearchFacesByImageResponse searchFacesByImageResponse = await rekognitionClient.SearchFacesByImageAsync(searchFacesByImageRequest);

            LambdaLogger.Log("Faces matching largest face in image from " + photo);
            LambdaLogger.Log("checking all faces");
            foreach (FaceMatch face in searchFacesByImageResponse.FaceMatches)
            {
                if (face.Similarity >= 99.0)
                {
                    faceId = face.Face.FaceId;
                }
                LambdaLogger.Log("FaceId: " + face.Face.FaceId + ", Similarity: " + face.Similarity);
            }
            LambdaLogger.Log(faceId == null ? "no face found" : "face found");
            return faceId;
        }


        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>

        public async Task<string> ProcessingGuestUserImageToIndexOrCheck(S3Event evnt, ILambdaContext context)
        {

            var s3Event = evnt.Records?[0].S3;
            string fileName = null;
            string bucket = null;
            if (s3Event == null)
            {
                return null;
            }

            try
            {
                var response = await this.S3Client.GetObjectMetadataAsync(s3Event.Bucket.Name, s3Event.Object.Key);

                fileName = s3Event.Object.Key;
                bucket = s3Event.Bucket.Name;

                LambdaLogger.Log(fileName + " Just added");

                string faceId = await searchFaceFromImage(fileName, bucket);
                LambdaLogger.Log("checking face id length ");
                bool isfaceIdnull = faceId == null;
                LambdaLogger.Log("is face id null " + isfaceIdnull);
                if (isfaceIdnull)
                {
                    LambdaLogger.Log("Adding face to collection");
                    faceId = await addFacesToCollectionasync(fileName, bucket);
                }
                // before indexing face make sure face is captured in this manner https://docs.aws.amazon.com/rekognition/latest/dg/recommendations-facial-input-images.html
                return faceId;

                //return response.Headers.ContentType;
            }
            catch (Exception e)
            {
                context.Logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                context.Logger.LogLine(e.Message);
                context.Logger.LogLine(e.StackTrace);
                throw;
            }
        }
    }
}
