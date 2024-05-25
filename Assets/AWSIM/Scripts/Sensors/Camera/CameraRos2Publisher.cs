using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROS2;
using System.Drawing;
using System.IO;
using UnityEngine;


namespace AWSIM
{
    public enum raw_or_compress
    {
        RAW, Compressed
    }

    /// <summary>
    /// Convert the data output from CameraSensor to ROS2 msg and Publish.
    /// </summary>
    [RequireComponent(typeof(CameraSensor))]
    public class CameraRos2Publisher : MonoBehaviour
    {
        [Header("ROS Topic parameters")]

        /// <summary>
        /// Topic name for Image msg.
        /// </summary>
        public string imageTopic = "/sensing/camera/traffic_light/image";

        /// <summary>
        /// Topic name for CameraInfo msg.
        /// </summary>
        public string cameraInfoTopic = "/sensing/camera/traffic_light/camera_info";

        /// <summary>
        /// Camera sensor frame id.
        /// </summary>
        public string frameId = "traffic_light_left_camera/camera_link";

        /// <summary>
        /// Publish type. Raw or Compressed.
        /// </summary>
        public raw_or_compress publish_type;

        /// <summary>
        /// QoS settings.
        /// </summary>
        public QoSSettings qosSettings = new QoSSettings()
        {
            ReliabilityPolicy = ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
            DurabilityPolicy = DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            HistoryPolicy = HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
            Depth = 1,
        };

        // Publishers
        IPublisher<sensor_msgs.msg.Image> imagePublisher;
        IPublisher<sensor_msgs.msg.CompressedImage> compressedImagePublisher;
        IPublisher<sensor_msgs.msg.CameraInfo> cameraInfoPublisher;
        sensor_msgs.msg.Image imageMsg;
        sensor_msgs.msg.CameraInfo cameraInfoMsg;
        sensor_msgs.msg.CompressedImage compressedImageMsg;

        CameraSensor sensor;

        void Start()
        {
            sensor = GetComponent<CameraSensor>();
            if (sensor == null)
            {
                throw new MissingComponentException("No active CameraSensor component found.");
            }
            if (publish_type == raw_or_compress.Compressed)
            {
                sensor.flip_image = true;
            }else
            {
                sensor.flip_image = false;
            }

            // Set callback
            sensor.OnOutputData += UpdateMessagesAndPublish;

            // Initialize msgs
            if(publish_type == raw_or_compress.RAW)
            {
                imageMsg = InitializeEmptyImageMsg();
            }
            else
            {
                compressedImageMsg = InitializeEmptyCompressedImageMsg();
            }
            cameraInfoMsg = InitializeEmptyCameraInfoMsg();

            // Create publishers
            var qos = qosSettings.GetQoSProfile();
            if (publish_type == raw_or_compress.RAW)
            {
                imagePublisher = SimulatorROS2Node.CreatePublisher<sensor_msgs.msg.Image>(imageTopic, qos);
            }
            else
            {
                compressedImagePublisher = SimulatorROS2Node.CreatePublisher<sensor_msgs.msg.CompressedImage>(imageTopic, qos);
            }
            cameraInfoPublisher = SimulatorROS2Node.CreatePublisher<sensor_msgs.msg.CameraInfo>(cameraInfoTopic, qos);
        }

        void UpdateMessagesAndPublish(CameraSensor.OutputData outputData)
        {
            if (!SimulatorROS2Node.Ok())
            {
                return;
            }

            // Update msgs
            UpdateImageMsg(outputData);
            UpdateCameraInfoMsg(outputData.cameraParameters);

            // Update msgs timestamp, timestamps should be synchronized in order to connect image and camera_info msgs
            var timeMsg = SimulatorROS2Node.GetCurrentRosTime();

            // Publish to ROS2
            if (publish_type == raw_or_compress.RAW)
            {
                imageMsg.Header.Stamp = timeMsg;
                imagePublisher.Publish(imageMsg);
            }
            else
            {
                compressedImageMsg.Header.Stamp = timeMsg;
                compressedImagePublisher.Publish(compressedImageMsg);
            }                
            cameraInfoMsg.Header.Stamp = timeMsg;
            cameraInfoPublisher.Publish(cameraInfoMsg);
        }

        private void UpdateImageMsg(CameraSensor.OutputData data)
        {
            if (publish_type == raw_or_compress.RAW)
            {
                if (imageMsg.Width != data.cameraParameters.width || imageMsg.Height != data.cameraParameters.height)
                {
                    imageMsg.Width = (uint)data.cameraParameters.width;
                    imageMsg.Height = (uint)data.cameraParameters.height;
                    imageMsg.Step = (uint)(data.cameraParameters.width * 3);

                    imageMsg.Data = new byte[data.cameraParameters.height * data.cameraParameters.width * 3];
                }

                imageMsg.Data = data.imageDataBuffer;
            }
            else
            {
                Texture2D texture = new Texture2D(data.cameraParameters.width, data.cameraParameters.height, TextureFormat.RGB24, false);
                texture.LoadRawTextureData(data.imageDataBuffer);
                texture.Apply();

                compressedImageMsg.Data = texture.EncodeToJPG();
            }
        }

        private void UpdateCameraInfoMsg(CameraSensor.CameraParameters cameraParameters)
        {
            if (cameraInfoMsg.Width != cameraParameters.width || cameraInfoMsg.Height != cameraParameters.height)
            {
                cameraInfoMsg.Width = (uint)cameraParameters.width;
                cameraInfoMsg.Height = (uint)cameraParameters.height;
            }

            // Update distortion parameters
            var D = cameraParameters.getDistortionParameters();
            if (!D.Equals(cameraInfoMsg.D))
            {
                cameraInfoMsg.D = cameraParameters.getDistortionParameters();
            }

            // Update camera matrix
            var K = cameraParameters.getCameraMatrix();
            if (!K.Equals(cameraInfoMsg.K))
            {
                for (int i = 0; i < K.Length; i++)
                    cameraInfoMsg.K[i] = K[i];
            }

            // Update projection matrix
            var P = cameraParameters.getProjectionMatrix();
            if (!P.Equals(cameraInfoMsg.P))
            {
                for (int i = 0; i < P.Length; i++)
                    cameraInfoMsg.P[i] = P[i];
            }
        }

        private sensor_msgs.msg.Image InitializeEmptyImageMsg()
        {
            return new sensor_msgs.msg.Image()
            {
                Header = new std_msgs.msg.Header()
                {
                    Frame_id = frameId
                },
                Encoding = "bgr8",
                Is_bigendian = 0,
            };
        }
        private sensor_msgs.msg.CompressedImage InitializeEmptyCompressedImageMsg()
        {
            return new sensor_msgs.msg.CompressedImage()
            {
                Header = new std_msgs.msg.Header()
                {
                    Frame_id = frameId
                },
                Format = "jpeg"
            };
        }

        private sensor_msgs.msg.CameraInfo InitializeEmptyCameraInfoMsg()
        {
            var message = new sensor_msgs.msg.CameraInfo()
            {
                Header = new std_msgs.msg.Header()
                {
                    Frame_id = frameId
                },
                Distortion_model = "plumb_bob",
                Binning_x = 0,
                Binning_y = 0,
                Roi = new sensor_msgs.msg.RegionOfInterest()
                {
                    X_offset = 0,
                    Y_offset = 0,
                    Height = 0,
                    Width = 0,
                    Do_rectify = false,
                }
            };

            // Set the rectification matrix for monocular camera
            var R = new double[] {
                1.0, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.0};

            for (int i = 0; i < R.Length; i++)
                message.R[i] = R[i];

            return message;
        }
    }
}