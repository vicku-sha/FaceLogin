// This file was auto-generated by ML.NET Model Builder. 

using Microsoft.ML.Data;

namespace FaceLoginML.Model
{
    public class ModelInput
    {
        [ColumnName("min_pose_roll"), LoadColumn(0)]
        public float Min_pose_roll { get; set; }


        [ColumnName("max_pose_roll"), LoadColumn(1)]
        public float Max_pose_roll { get; set; }


        [ColumnName("min_pose_pitch"), LoadColumn(2)]
        public float Min_pose_pitch { get; set; }


        [ColumnName("max_pose_pitch"), LoadColumn(3)]
        public float Max_pose_pitch { get; set; }


        [ColumnName("min_pose_yaw"), LoadColumn(4)]
        public float Min_pose_yaw { get; set; }


        [ColumnName("max_pose_yaw"), LoadColumn(5)]
        public float Max_pose_yaw { get; set; }


        [ColumnName("eye_open"), LoadColumn(6)]
        public float Eye_open { get; set; }


        [ColumnName("max_koef_lips"), LoadColumn(7)]
        public float Max_koef_lips { get; set; }


        [ColumnName("min_koef_lips"), LoadColumn(8)]
        public float Min_koef_lips { get; set; }


        [ColumnName("human"), LoadColumn(9)]
        public string Human { get; set; }


    }
}