using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FaceRecognitionDotNet;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using FaceRecognitionDotNet.Extensions;
using System.Drawing.Imaging;
using FaceLoginML.Model;
using System.Drawing.Drawing2D;

namespace FaceLogin
{
    public partial class FrmLogin : Form
    {
        //СЕМАФОР
        object locker = new object();

        //Определение Базы Данных
        private ApplicationContext db = new ApplicationContext();

        Stopwatch stopWatch = new Stopwatch();  //Таймер

        private static FaceRecognition faceRecognition;

        private int rec_delay = 1000; //Задержка между циклами распознования

        private int frame_count = 0; //Счетчик фреймов
        private int rec_frame = 15; //На каком фрейме мы производим распознавание лиц

        private PredictorModel enc_predictorModel = PredictorModel.Large; //Модель кодирования лиц
        private PredictorModel lm_predictorModel = PredictorModel.Large; //Модель выставления точек на лице (лэндмарков)

        private Model enc_model = Model.Hog; //Метод кодировния лиц
        private Model loc_model = Model.Hog; //Метод распознования положения лиц

        public List<UserFace> userFaces = new List<UserFace>(); //Список распознаных лиц
        public List<UserFace> last_userFaces = new List<UserFace>(); //Список распознаных лиц перед очисткой для распознования

        double compare_tolerance = 0.5; //Допуск при распозновании лиц(меньше - строже)
        public bool recognition_in_progress = false; //флаг активного распознования
        public bool load_faces_in_progress = false; //флаг загрузки лиц
        public bool ext_recognition_in_progress = false; //флаг дополнительного распознования

        public int verdict = 0;  //Вердикт
        public int foto = 0; //Сколько раз было обнаружено фото из количества проверок
        public int successful = 0; //Сколько раз пустили (человека)
        public int failed = 0; //Сколько раз не допустили


        private VideoCapture capture;

        private BackgroundWorker backgroundWorker = new BackgroundWorker(); //Воркер, плановая задача
        private AutoResetEvent doneEvent = new AutoResetEvent(false);

        private Mat frame = new Mat();   

        private List<Location> curr_locations = null;
        private FaceRecognitionDotNet.Image curr_image = null;
        
        //Список Энкодированных лиц
        private List<EncodingFace> EncodingFaces = new List<EncodingFace>();
        private List<FaceEncoding> know_faces_no_name = new List<FaceEncoding>();

        public FrmLogin()
        {
            System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = customCulture;

            InitializeComponent();

            faceRecognition = FaceRecognition.Create("models");

            faceRecognition.CustomEyeBlinkDetector = new EyeAspectRatioLargeEyeBlinkDetector(0.2, 0.2);

            var rollFile = Path.Combine("models", "300w-lp-roll-krls_0,001_0,1.dat");
            var pitchFile = Path.Combine("models", "300w-lp-pitch-krls_0,001_0,1.dat");
            var yawFile = Path.Combine("models", "300w-lp-yaw-krls_0,001_0,1.dat");
            faceRecognition.CustomHeadPoseEstimator = new SimpleHeadPoseEstimator(rollFile, pitchFile, yawFile);

            try
            {
                capture = new VideoCapture();
            }
            catch (NullReferenceException ex)
            {
                MessageBox.Show(ex.Message);
            }

            if (capture != null)
            {
                capture.ImageGrabbed += Capture_ImageGrabbed;
                capture.Start();
            }

            backgroundWorker.WorkerSupportsCancellation = true; //Включение поддержки воркера
            backgroundWorker.DoWork += Recognition;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
        }


        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) //
        {
            if (e.Cancelled) //Мы отменили (закрыли программу)
            {
                Console.WriteLine("BackgroundWorker stop!");
            }
            else if (e.Error != null)  //Закончился с ошибкой
            {
                Task.Run(() => { StartWorker(); });
            }
            else //Закончился самостоятельно 
            {
                Task.Run(() => { StartWorker(); });
            }
        }


        private void StartWorker()
        {
            Thread.Sleep(3000);

            if (backgroundWorker != null && backgroundWorker.IsBusy == false)
            {
                backgroundWorker.RunWorkerAsync();
            }
        }


        private void Recognition(object sender, DoWorkEventArgs e)
        {
            bool while_condition = true;
            while (while_condition)
            {
                try
                {
                    recognition_in_progress = true;
                    //Таймер начало
                    stopWatch.Start();

                    if (!(backgroundWorker == null))
                    {
                        if (backgroundWorker.CancellationPending)
                        {
                            e.Cancel = true;
                            while_condition = false;
                            doneEvent.Set();
                            break;
                        }

                    }
                    else
                    {
                        e.Cancel = true;
                        while_condition = false;
                        doneEvent.Set();
                        break;
                    }

                    if (curr_image != null && curr_locations != null &&
                        curr_locations.Count > 0 && EncodingFaces.Count > 0)
                    {
                        FaceRecognitionDotNet.Image cur_image = curr_image;
                        IEnumerable<Location> cur_locations = curr_locations;
                        
                        last_userFaces = new List<UserFace>(userFaces);
                        userFaces.Clear();
                        var faceLocations = cur_locations.ToList();

                        //Console.WriteLine(EncodingFaces.Count);
                        IEnumerable<FaceEncoding> faces;
                        
                        lock (locker)
                        { 
                            faces = faceRecognition.FaceEncodings(curr_image, curr_locations, 1, enc_predictorModel, enc_model);
                        }
                        //Console.WriteLine(know_face.NameUser);



                        int face_index = 0;
                        foreach (var face in faces)
                        {
                            IEnumerable<bool> checks;
                            
                            lock (locker)
                            {
                                checks = FaceRecognition.CompareFaces(know_faces_no_name, face, compare_tolerance);
                            }
                                int count = 0;

                            foreach (var check in checks)
                            {
                                //Console.WriteLine(check);

                                bool closed = false;
                                HeadPose pose = null;

                                if (check)
                                {
                                    //ОСНОВНОЙ КОД ДЛЯ ВЫСТАВЛЕНИЯ ТОЧЕК (ЛЭНДМАРКОВ) НА РАСПОЗНАННОМ ЛИЦЕ//
                                    var faceLoc = new List<Location>();
                                    faceLoc.Add(faceLocations[face_index]);
                                    IDictionary<FacePart, IEnumerable<FacePoint>> faceLandmark;

                                    lock (locker)
                                    {
                                        faceLandmark = faceRecognition.FaceLandmark(curr_image,
                                        faceLoc, lm_predictorModel).ToArray().FirstOrDefault();  //Получение точек на лице

                                        if (faceLandmark != null)
                                        {
                                            faceRecognition.EyeBlinkDetect(faceLandmark, out var leftBlink, out var rightBlink); //Метод получения открытых или закрытых глаз
                                            closed = leftBlink && rightBlink;
                                            //Console.WriteLine($"Глаза закрыты {closed}");

                                            pose = faceRecognition.PredictHeadPose(faceLandmark);
                                        }
                                    }
                                        

                                    userFaces.Add(new UserFace
                                    {
                                        NameUser = EncodingFaces[count].NameUser,
                                        LocationUser = faceLocations[face_index],
                                        RecognitionUser = true,
                                        Landmark = faceLandmark,
                                        EyeClose = closed,
                                        HeadPose = pose,
                                        FaceEncoding = EncodingFaces[count].FaceEncoding
                                    });

                                    break;
                                }

                                count++;
                            }

                            face_index++;
                        }

                        userFaces = userFaces.Distinct().ToList();
                        userFaces.Sort((x, y) => x.LocationUser.Left.CompareTo(y.LocationUser.Left));

                        //Console.WriteLine("-------------------------------");
                    }                   

                    //Таймер конец
                    stopWatch.Stop();
                    TimeSpan ts = stopWatch.Elapsed;
                    //Console.WriteLine(ts.Milliseconds);
                    if (ts.TotalMilliseconds < rec_delay)
                    {
                        Thread.Sleep(rec_delay - ts.Milliseconds);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("BackgroundWorker_DoWork: " + ex.Message);
                    userFaces.Clear();
                    while_condition = false;
                    doneEvent.Set();
                    break;
                }
                finally
                {                    
                    recognition_in_progress = false;              
                }

            }
            
        }


        private void VerificationNotStaticImage(UserFace cur_face, int count_checks)
        {
            //Console.WriteLine("Старт верификации");

            if (cur_face == null)
            {
                return;
            }

            //Console.WriteLine("текущее лицо существует");

            ext_recognition_in_progress = true;
            float min_pose_roll = float.MaxValue;
            float max_pose_roll = float.MinValue;
            float min_pose_pitch = float.MaxValue;
            float max_pose_pitch = float.MinValue;
            float min_pose_yaw = float.MaxValue;
            float max_pose_yaw = float.MinValue;
            float eye_open = 0F;
            float max_koef_lips = float.MinValue;
            float min_koef_lips = float.MaxValue;

            //Оригинальный коэффициент губ
 
            var OfacePoints = new List<FacePoint>(); //Список точек по индексу
            foreach (var value in cur_face.Landmark.Values) OfacePoints.AddRange(value);
            OfacePoints = OfacePoints.Distinct().ToList();

            var OlenghtLips = LengthBetweenPoint(OfacePoints.Find(point => point.Index == 49), OfacePoints.Find(point => point.Index == 55));
            //Console.WriteLine($"Оригинальная ширина губ { OlenghtLips }");

            var OheightLips = LengthBetweenPoint(OfacePoints.Find(point => point.Index == 52), OfacePoints.Find(point => point.Index == 58));
            //Console.WriteLine($"Ориганальная высота губ { OheightLips }");

            var OkoefLips = OheightLips / OlenghtLips;
            //Console.WriteLine($"Оригинальный коэффициент губ { OkoefLips }");


            for (int i = 0; i < count_checks; i++)
            {
                FaceRecognitionDotNet.Image searchImage;
                List<Location> faceLocationsDotNet;
                IEnumerable<FaceEncoding> faces;
                IEnumerable<bool> checks;
                
                lock (locker)
                {
                    searchImage = FaceRecognition.LoadImage(frame.ToBitmap()); 

                    faceLocationsDotNet = faceRecognition.FaceLocations(searchImage, 1, loc_model).ToList();
                    faceLocationsDotNet.Sort((x, y) => x.Left.CompareTo(y.Left));

                    faces = faceRecognition.FaceEncodings(searchImage, faceLocationsDotNet, 1, enc_predictorModel, enc_model);
                    checks = FaceRecognition.CompareFaces(faces, cur_face.FaceEncoding, compare_tolerance);
                }
               
                int index = 0;
                foreach (var check in checks)
                {
                    bool closed = false;
                    HeadPose pose = null;

                    if (check)
                    {
                        var faceLoc = new List<Location>();
                        faceLoc.Add(faceLocationsDotNet[index]);

                        IDictionary<FacePart, IEnumerable<FacePoint>> faceLandmark;
                        lock (locker)
                        {
                            faceLandmark = faceRecognition.FaceLandmark(searchImage, faceLoc, lm_predictorModel).ToArray().FirstOrDefault();  //Получение точек на лице

                            if (faceLandmark != null)
                            {
                                faceRecognition.EyeBlinkDetect(faceLandmark, out var leftBlink, out var rightBlink); //Метод получения открытых или закрытых глаз
                                closed = leftBlink && rightBlink;
                                //Console.WriteLine($"Глаза закрыты {closed}");

                                pose = faceRecognition.PredictHeadPose(faceLandmark);
                            }
                        }

                        //Console.WriteLine(i);

                        //проверка по глазам
                        if (closed!=cur_face.EyeClose)
                        {
                            eye_open = 1F;
                        }

                        //проверка по позиции лица
                        var roll = (float)Math.Abs(pose.Roll - cur_face.HeadPose.Roll);
                        if (roll>max_pose_roll)
                        {
                            max_pose_roll = roll;
                        }
                        if (roll < min_pose_roll)
                        {
                            min_pose_roll = roll;
                        }

                        var pitch = (float)Math.Abs(pose.Pitch - cur_face.HeadPose.Pitch);
                        if (pitch > max_pose_roll)
                        {
                            max_pose_pitch = pitch;
                        }
                        if (pitch < min_pose_pitch)
                        {
                            min_pose_pitch = pitch;
                        }
                        var yaw = (float)Math.Abs(pose.Yaw - cur_face.HeadPose.Yaw);
                        if (yaw > max_pose_yaw)
                        {
                            max_pose_yaw = yaw;
                        }
                        if (yaw < min_pose_yaw)
                        {
                            min_pose_yaw = yaw;
                        }

                        //проверка по губам                        
                        //Остальные фреймы
                        var facePoints = new List<FacePoint>(); //Список точек по индексу
                        foreach (var value in faceLandmark.Values) facePoints.AddRange(value);
                        facePoints = facePoints.Distinct().ToList();

                        var lenghtLips = LengthBetweenPoint(facePoints.Find(point => point.Index == 49), facePoints.Find(point => point.Index == 55));
                        //Console.WriteLine($"Ширина губ { lenghtLips }");

                        var heightLips = LengthBetweenPoint(facePoints.Find(point => point.Index == 52), facePoints.Find(point => point.Index == 58));
                        //Console.WriteLine($"Высота губ { heightLips }");

                        var koefLips = heightLips / lenghtLips;
                        //Console.WriteLine($"Коэффициент губ { koefLips }");

                        //Console.WriteLine($"Коэффициент { (Math.Abs(OkoefLips - koefLips) / OkoefLips)*100  } %");

                        var koef = (float)Math.Abs(OkoefLips - koefLips);

                        if (koef > max_koef_lips)
                        {
                            max_koef_lips = koef;
                        }
                        if (yaw < min_koef_lips)
                        {
                            min_koef_lips = koef;
                        }

                        //дальше проверенные лица смотреть незачем
                        break;
                    }
                }

                //////////////////////////                         //////////////////////////
                //////////////////////////КНОПКА ОБУЧЕНИЯ НЕЙРОСЕТИ//////////////////////////
                //////////////////////////                         //////////////////////////

                if (checkBox1.CheckState == CheckState.Unchecked) //Обучение не активно
                {
                    checkBox1.BackColor = Color.Orange;

                    // Add input data
                    var input = new ModelInput()
                    {
                        Min_pose_roll = min_pose_roll,
                        Max_pose_roll = max_pose_roll,
                        Min_pose_pitch = min_pose_pitch,
                        Max_pose_pitch = max_pose_pitch,
                        Min_pose_yaw = min_pose_yaw,
                        Max_pose_yaw = max_pose_yaw,
                        Eye_open = eye_open,
                        Max_koef_lips = max_koef_lips,
                        Min_koef_lips = min_koef_lips,
                    };

                    // Load model and predict output of sample data
                    ModelOutput result = ConsumeModel.Predict(input);

                    if (result.Prediction == "1")
                    {
                        try
                        { 
                            label2.BeginInvoke((MethodInvoker)(() =>
                            {
                                this.label2.Text = "Человек";
                                verdict++;
                            }
                            ));
                        }
                        catch (Exception)
                        {

                            Console.WriteLine("Ошибка label2.Text");
                        }

                        try
                        {
                            label3.BeginInvoke((MethodInvoker)(() =>
                            {
                                this.label3.Text = Math.Round(double.Parse(result.Score[0].ToString()), 3).ToString();
                            }
                            ));
                        }
                        catch (Exception)
                        {

                            Console.WriteLine("Ошибка label3.Text");
                        } 
                    }
                    else
                    {
                        try
                        {
                            label2.BeginInvoke((MethodInvoker)(() =>
                            {
                                this.label2.Text = "Фотография";
                            }
                            ));
                        }
                        catch (Exception)
                        {

                            Console.WriteLine("Ошибка label2.Text");
                        }

                        try
                        {
                            label3.BeginInvoke((MethodInvoker)(() =>
                            {
                                this.label3.Text = Math.Round(double.Parse(result.Score[0].ToString()), 3).ToString();
                            }
                            ));
                        }
                        catch (Exception)
                        {

                            Console.WriteLine("Ошибка label3.Text");
                        }
                    }

                    //Console.WriteLine($"Результат: {result.Prediction } {result.Score}");
                }
                
                //---------------------------------------------------------------------------------------------------

                if (checkBox1.CheckState == CheckState.Checked)  //Обучение запущено
                {
                    checkBox1.BackColor = Color.Yellow;

                    if (radioButton1.Checked == true)
                    {
                        float human = 1F;
                        string newFileName = "data.csv";

                        string data = $"{min_pose_roll},{max_pose_roll},{min_pose_pitch}," +
                            $"{max_pose_pitch},{min_pose_yaw},{max_pose_yaw}," +
                            $"{eye_open},{max_koef_lips},{min_koef_lips},{human}{Environment.NewLine}";
                        if (!File.Exists(newFileName))
                        {
                            string dataHeader = $"min_pose_roll,max_pose_roll,min_pose_pitch," +
                            $"max_pose_pitch,min_pose_yaw,max_pose_yaw," +
                            $"eye_open,max_koef_lips,min_koef_lips,human{Environment.NewLine}";
                            File.WriteAllText(newFileName, dataHeader);
                        }

                        File.AppendAllText(newFileName, data);
                    }

                    if (radioButton2.Checked == true)
                    {
                        float human = 0F;
                        string newFileName = "data.csv";

                        string data = $"{min_pose_roll},{max_pose_roll},{min_pose_pitch}," +
                            $"{max_pose_pitch},{min_pose_yaw},{max_pose_yaw}," +
                            $"{eye_open},{max_koef_lips},{min_koef_lips},{human}{Environment.NewLine}";
                        if (!File.Exists(newFileName))
                        {
                            string dataHeader = $"min_pose_roll,max_pose_roll,min_pose_pitch," +
                            $"max_pose_pitch,min_pose_yaw,max_pose_yaw," +
                            $"eye_open,max_koef_lips,min_koef_lips,human{Environment.NewLine}";
                            File.WriteAllText(newFileName, dataHeader);
                        }

                        File.AppendAllText(newFileName, data);
                    }
                }

                //////////////////////////                         //////////////////////////
                //////////////////////////-------------------------//////////////////////////
                //////////////////////////                         //////////////////////////
            }

            if (verdict >= 3)
            {
                try
                {
                    label4.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label4.Text = "ДОПУЩЕН";
                        successful++;
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label4.Text");
                }


                foto = count_checks - verdict;

                try
                {
                    label4.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label6.Text = $"Фото: {foto}";
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label6.Text");
                }


                try
                {
                    label7.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label7.Text = $"+: {successful}";
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label7.Text");
                }
            }
            else
            {
                try
                {
                    label4.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label4.Text = "НЕ ДОПУЩЕН";
                        failed++;
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label4.Text");
                }


                foto = count_checks - verdict;

                try
                {
                    label4.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label6.Text = $"Фото: {foto}";
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label6.Text");
                }


                try
                {
                    label8.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.label8.Text = $"-: {failed}";
                    }
                    ));
                }
                catch (Exception)
                {

                    Console.WriteLine("Ошибка label8.Text");
                }
            }

            verdict = 0;
            ext_recognition_in_progress = false;
        }



        private void Capture_ImageGrabbed(object sender, EventArgs e)
        {
            try
            {
                if (capture != null && capture.Ptr != IntPtr.Zero)
                {
                    Mat preFrame = new Mat();
                    
                    capture.Retrieve(preFrame); //Захват фрейма
                    //Console.Write(preFrame.Size.Width);
                    //Console.Write(" ");
                    //Console.WriteLine(preFrame.Size.Height);
                    CvInvoke.Flip(preFrame, preFrame, FlipType.Horizontal); //Переворот картинки
                    
                    CvInvoke.Resize(preFrame, frame, new Size(640, 480), 0, 0, Inter.Cubic); //Приведение к соотношению 4:3 для любой камеры (Это 0,3 Мр)
                   
                    List<Location> faceLocationsDotNet = null;

                    if (frame_count == rec_frame)
                    {
                        //Принудительный сборщик мусора
                        //GC.Collect();

                        FaceRecognitionDotNet.Image searchImage;
                        lock (locker)
                        {
                            searchImage = FaceRecognition.LoadImage(frame.ToBitmap());
                            faceLocationsDotNet = faceRecognition.FaceLocations(searchImage, 1, loc_model).ToList();
                        }
                        
                        faceLocationsDotNet.Sort((x, y) => x.Left.CompareTo(y.Left));
                        if (faceLocationsDotNet != null && faceLocationsDotNet.Count > 0 && faceLocationsDotNet.Count < 4)
                        {
                            //Если найдены лицо или лица, то мы сохраняем их положение и картинку фрейма
                            curr_locations = new List<Location>(faceLocationsDotNet);
                            curr_image = searchImage;
                        }
                        else
                        {
                            if (curr_locations!=null)
                            {
                                curr_locations.Clear();
                            }
                            
                        }

                        frame_count = 0;
                    }
                    else
                    {
                        faceLocationsDotNet = curr_locations;
                    }

                    Image<Bgr, byte> image = frame.ToImage<Bgr, Byte>();

                    if (curr_locations != null && curr_locations.Count > 0)
                    {   
                        List<UserFace> cur_userFaces = null;
                        if (recognition_in_progress)
                        {
                            cur_userFaces = new List<UserFace>(last_userFaces);
                        }
                        else
                        {
                            cur_userFaces = new List<UserFace>(userFaces);
                        }
                        
                        foreach (var face in curr_locations)
                        {
                            string name_user = "Unknown";

                            var colorUser = new Bgr(Color.Red);

                            if (cur_userFaces != null && cur_userFaces.Count != 0)
                            {
                                int count_userFace = int.MaxValue;
                                int distant_min_x = int.MaxValue;
                                int count_dop = 0;

                                foreach (var item in cur_userFaces) //Для выведения рамки 
                                {
                                    var perem = Math.Abs(item.LocationUser.Left - face.Left);

                                    if (perem < distant_min_x && 
                                        perem < (Math.Abs(item.LocationUser.Right - item.LocationUser.Left)/2))
                                    {
                                        count_userFace = count_dop;
                                        distant_min_x = perem;
                                    }
                                    count_dop++;

                                    //Console.WriteLine(perem);
                                }
                                //Console.WriteLine(count_userFace);

                                try
                                {
                                    if (count_userFace != int.MaxValue && cur_userFaces[count_userFace].RecognitionUser)
                                    {
                                        //ЗАПУСК ДОПОЛНИТЕЛЬНЫХ ПРОВЕРОК

                                        if (ext_recognition_in_progress != true)
                                        {
                                            Task.Run(() =>
                                            {
                                                try
                                                {
                                                    VerificationNotStaticImage(cur_userFaces[count_userFace], 5); //Здесь число - количество дополнительных проверок
                                                }
                                                catch (Exception ex)
                                                {

                                                    Console.WriteLine($"Ошибка дополнительных проверок {ex.Message}");
                                                }
                                                finally
                                                {
                                                    ext_recognition_in_progress = false;
                                                }

                                            });
                                        }

                                        name_user = cur_userFaces[count_userFace].NameUser;

                                        colorUser = new Bgr(Color.Green);


                                        //РИСУЕМ ТОЧКИ
                                        if (cur_userFaces[count_userFace].Landmark != null)
                                        {
                                            foreach (FacePart key in cur_userFaces[count_userFace].Landmark.Keys)
                                            {
                                                var value = cur_userFaces[count_userFace].Landmark[key];

                                                foreach (var item in value)
                                                {
                                                    image.Draw(new Rectangle(new System.Drawing.Point(item.Point.X, item.Point.Y), new Size(1, 1)), colorUser, 3);
                                                }
                                            }


                                            //РИСУЕМ ПОЗИЦИЮ ЛИЦА
                                            try
                                            {
                                                DrawAxis(image, cur_userFaces[count_userFace].Landmark, cur_userFaces[count_userFace].HeadPose.Roll,
                                                   cur_userFaces[count_userFace].HeadPose.Pitch, cur_userFaces[count_userFace].HeadPose.Yaw, 120);

                                            }
                                            catch (Exception ex)
                                            {

                                                Console.WriteLine($"Ошибка прорисовки позы {ex.Message}"); ;
                                            }
                                        }

                                    }
                                    else
                                    {
                                        colorUser = new Bgr(Color.Red);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    colorUser = new Bgr(Color.Red);
                                    Console.WriteLine($"Ошибка красной рамки {ex.Message}"); ;
                                }


                                if ((face.Right - face.Left) >= 125)
                                {
                                    image.Draw(name_user, new System.Drawing.Point(face.Left + 10, face.Bottom - 10), FontFace.HersheyComplex, 1, colorUser);
                                }
                                else
                                {
                                    image.Draw(name_user, new System.Drawing.Point(face.Left + 10, face.Bottom - 10), FontFace.HersheyComplex, 0.5, colorUser);
                                }
                            }
                            else
                            {
                                if ((face.Right - face.Left) >= 125)
                                {
                                    image.Draw(name_user, new System.Drawing.Point(face.Left + 10, face.Bottom - 10), FontFace.HersheyComplex, 1, colorUser);
                                }
                                else
                                {
                                    image.Draw(name_user, new System.Drawing.Point(face.Left + 10, face.Bottom - 10), FontFace.HersheyComplex, 0.5, colorUser);
                                }
                            }
                            
                            image.Draw(new Rectangle(face.Left, face.Top, face.Right - face.Left, face.Bottom - face.Top), colorUser, 3); 
                        }
                    }

                    try
                    {
                        captureBox.BeginInvoke((MethodInvoker)(() =>
                        {
                            this.captureBox.Image = image.ToBitmap();
                        }
                        ));
                    }
                    catch (Exception)
                    {

                        Console.WriteLine("Ошибка captureBox.Image");
                    }
                    frame_count++;
                }
             
            }
            catch (NullReferenceException ex)
            {
                MessageBox.Show(ex.Message);
                if (capture == null) return;
                capture.Stop();
                capture.Dispose();
            }
        }
               

        private void BtnSaveUser_Click(object sender, EventArgs e) //Кнопка Запомнить пользователя
        {
            try
            {
                FaceRecognitionDotNet.Image cur_image = curr_image;
                IEnumerable<Location> cur_locations = curr_locations;

                if (cur_locations != null)
                {
                    IEnumerable<FaceRecognitionDotNet.Image> images;
                    
                    lock (locker)
                    {
                        images = FaceRecognition.CropFaces(cur_image, cur_locations); //Получили все вырезанные лица (вместо полной картинки)
                    }
                    

                    if (images.Count() > 0)
                    {                        

                        string nameus = InputBox.Show("Введите ФИО пользователя: ");

                        if (nameus != string.Empty)
                        {
                            //Сохранияем первое лицо и имя в базе данных
                            Face face = new Face { NameUser = nameus, PhotoUser = ImageToByte(images.ToArray().FirstOrDefault().ToBitmap())};
                            db.Faces.Add(face);
                            db.SaveChanges();
                            LoadFaces();
                        }
                    }

                }
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Ошибка запоминания {ex.Message}");
            }

        }


        //Изображение в массив байтов
        public static byte[] ImageToByte(System.Drawing.Image image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                return stream.ToArray();
            }
        }

        //Массив байтов в изображение
        public static System.Drawing.Image ByteToImage(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                System.Drawing.Image image = System.Drawing.Image.FromStream(stream);
                return image;
            }
        }

        //Закрытия основной формы
        private void FrmLogin_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings1.Default.WindowWidth = this.Size.Width;
            Settings1.Default.WindowHeight = this.Size.Height;
            Settings1.Default.Save();
            Settings1.Default.Upgrade();

            //Избавляемся от устройства захвата
            if (capture != null)
            {
                capture.Stop();
                capture.Dispose();
            }

            try
            {
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения БД {ex.Message}");
            }
            try
            {
                db.Dispose();
            }
            catch (Exception)
            {                
            }
        }

        //Открытие формы     
        private void FrmLogin_Load(object sender, EventArgs e)
        {
            this.Size = new Size(Settings1.Default.WindowWidth, Settings1.Default.WindowHeight);

            //Создаем базу если она отсутвует
            db.Database.EnsureCreated();
            //Назначам авторастягивание строк по высоте и ширине
            dataGridViewFaces.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            dataGridViewFaces.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCellsExceptHeader;            
            //Прогружаем таблицу Faces
            db.Faces.Load();
            //Привязываем dataGridViewFaces
            dataGridViewFaces.DataSource = db.Faces.Local.ToBindingList();
            LoadFaces();

            backgroundWorker.RunWorkerAsync(); //Запуск Воркера
        }



        //Загрузка известных лиц
        private void LoadFaces()
        {
            //Console.WriteLine($"load_faces_in_progress: {load_faces_in_progress}");
            if (load_faces_in_progress==false)
            {
                load_faces_in_progress = true;

                try
                {
                    List<EncodingFace> cur_enc_face = new List<EncodingFace>();
                    var faces = db.Faces.Select(c => new { c.NameUser, c.PhotoUser }).ToList();

                    foreach (var face in faces)
                    {
                        // Энкодирование лица
                        FaceRecognitionDotNet.Image image;
                        
                        lock (locker)
                        {
                            image = FaceRecognition.LoadImage((Bitmap)ByteToImage(face.PhotoUser));
                        }
                        

                        //Console.WriteLine(face.NameUser);
                        try
                        {
                            List<FaceEncoding> faceEncoding;

                            lock (locker)
                            {
                                faceEncoding = faceRecognition.FaceEncodings(image, null, 1, enc_predictorModel, enc_model).ToList();
                            }
                               

                            FaceEncoding enc_face = faceEncoding.FirstOrDefault();
                            if (enc_face != null)
                            {
                                cur_enc_face.Add(new EncodingFace
                                {
                                    FaceEncoding = enc_face,
                                    NameUser = face.NameUser
                                });
                                Console.WriteLine($"{face.NameUser} лицо загружено");
                            }
                            else
                            {
                                Console.WriteLine($"{face.NameUser} лицо не энкодировано");
                            }

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{face.NameUser} ошибка распознования {ex.Message}");
                        }

                    }
                    EncodingFaces = new List<EncodingFace>(cur_enc_face);
                    know_faces_no_name.Clear();

                    foreach (var item in EncodingFaces)
                    {
                        know_faces_no_name.Add(item.FaceEncoding);  //Копия из БД по лицам (только без имен)
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LoadFaces {ex.Message}");
                }
                finally
                {
                    load_faces_in_progress = false;
                }
            } 
        }


        //Класс лица для распознования
        public class UserFace
        {
            public string NameUser { get; set; }
            public Location LocationUser { get; set; }
            public bool RecognitionUser { get; set; }
            public IDictionary<FacePart, IEnumerable<FacePoint>> Landmark { get; set; }
            public bool EyeClose { get; set; } //false - глаза открыты true - глаза закрыты
            public HeadPose HeadPose { get; set; }
            public FaceEncoding FaceEncoding { get; set; }
        }

        public class EncodingFace
        {
            public string NameUser { get; set; }
            public FaceEncoding FaceEncoding { get; set; }
        }


        private void SaveChangesLocal()
        {
            try
            {
                db.SaveChanges();
                LoadFaces();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }


        private static void DrawAxis(Image<Bgr, byte> image, IDictionary<FacePart, IEnumerable<FacePoint>> landmark, double roll, double pitch, double yaw, uint size)
        {
            // plot_pose_cube
            pitch = pitch * Math.PI / 180;
            yaw = -(yaw * Math.PI / 180);
            roll = roll * Math.PI / 180;

            var facePoints = new List<FacePoint>();
            foreach (var value in landmark.Values) facePoints.AddRange(value);
            facePoints = facePoints.Distinct().ToList();

            var center = facePoints.Find(point => point.Index == 33);
            var tdx = center.Point.X;
            var tdy = center.Point.Y;

            // X-Axis pointing to right. drawn in red
            var x1 = size * (Math.Cos(yaw) * Math.Cos(roll)) + tdx;
            var y1 = size * (Math.Cos(pitch) * Math.Sin(roll) + Math.Cos(roll) * Math.Sin(pitch) * Math.Sin(yaw)) + tdy;

            // Y-Axis | drawn in green
            // v
            var x2 = size * (-Math.Cos(yaw) * Math.Sin(roll)) + tdx;
            var y2 = size * (Math.Cos(pitch) * Math.Cos(roll) - Math.Sin(pitch) * Math.Sin(yaw) * Math.Sin(roll)) + tdy;

            // Z-Axis (out of the screen) drawn in blue
            var x3 = size * (Math.Sin(yaw)) + tdx;
            var y3 = size * (-Math.Cos(yaw) * Math.Sin(pitch)) + tdy;

            image.Draw(new LineSegment2D(new System.Drawing.Point(tdx, tdy), new System.Drawing.Point((int)x1,(int)y1)),
                new Bgr(Color.Red), 3);
            image.Draw(new LineSegment2D(new System.Drawing.Point(tdx, tdy), new System.Drawing.Point((int)x2, (int)y2)),
                new Bgr(Color.Green), 3);
            image.Draw(new LineSegment2D(new System.Drawing.Point(tdx, tdy), new System.Drawing.Point((int)x3, (int)y3)),
                new Bgr(Color.Blue), 3);
          
        }

        private void dataGridViewFaces_CellValueChanged(object sender, DataGridViewCellEventArgs e) //Изменение
        {
            //SaveChangesLocal();
        }


        private void dataGridViewFaces_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)  //Удаление
        {
            //SaveChangesLocal();
        }


        static double LengthBetweenPoint(FacePoint p1, FacePoint p2)
        {
            return Math.Sqrt(Math.Pow(p2.Point.X - p1.Point.X, 2) + Math.Pow(p2.Point.Y - p1.Point.Y, 2));
        }


        private void button1_Click(object sender, EventArgs e)  //Сохранить изменения
        {
            SaveChangesLocal();
        }


        private void button2_Click(object sender, EventArgs e)  //Загрузить данные в БД 
        {
            using (var choofdlog = new OpenFileDialog()) //ANY dialog
            {
                var imageExtensions = string.Join(";", ImageCodecInfo.GetImageDecoders().Select(ici => ici.FilenameExtension));
                choofdlog.Filter = string.Format("Images|{0}", imageExtensions);
                choofdlog.FilterIndex = 1;
                choofdlog.Multiselect = true;

                if (choofdlog.ShowDialog() == DialogResult.OK)
                {
                    foreach (var item in choofdlog.FileNames)
                    {
                        Console.WriteLine(item);

                        try
                        {
                            FaceRecognitionDotNet.Image down_image = FaceRecognition.LoadImage((Bitmap)System.Drawing.Image.FromFile(item));
                            IEnumerable<Location> down_locations = faceRecognition.FaceLocations(down_image);

                            if (down_locations != null)
                            {
                                IEnumerable<FaceRecognitionDotNet.Image> images = FaceRecognition.CropFaces(down_image, down_locations); //Получили все вырезанные лица (вместо полной картинки)

                                if (images.Count() > 1)
                                {
                                    foreach (var image in images) //вырезанное лицо из картинки
                                    {
                                        string nameus = InputBoxWithImage.Show("Введите ФИО пользователя: ", image.ToBitmap());

                                        if (nameus != string.Empty)
                                        {
                                            //Сохранияем первое лицо и имя в базе данных
                                            Face face = new Face { NameUser = nameus, PhotoUser = ImageToByte(image.ToBitmap()) };
                                            db.Faces.Add(face);
                                            db.SaveChanges();
                                        }
                                    }  
                                }
                                else if(images.Count() == 1)
                                {
                                    try
                                    {
                                        string nameUserInFoto = Path.GetFileName(item).Split('.')[0];

                                        //Сохранияем первое лицо и имя в базе данных
                                        Face face = new Face { NameUser = nameUserInFoto, PhotoUser = ImageToByte(images.ToArray().FirstOrDefault().ToBitmap()) };
                                        db.Faces.Add(face);
                                        db.SaveChanges();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Ошибка имени фото {ex.Message}");
                                    }
                                    
                                }

                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка запоминания {ex.Message}");
                        }
                    }
                }
            }
        }


        private void checkBox1_CheckedChanged(object sender, EventArgs e) 
        {
           
        }


        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabPage1)
            {
                Console.WriteLine("tabPage1");
                capture.Start();
                Task.Run(() => { StartWorker(); });
            }

            if(tabControl1.SelectedTab == tabPage2)
            {
                Console.WriteLine("tabPage2");
                capture.Stop();
                backgroundWorker.CancelAsync();
            }
        }


        //public static Bitmap ResizeImage(Bitmap image, int width, int height)  //Ресайз изображения
        //{
        //    var destRect = new Rectangle(0, 0, width, height);
        //    var destImage = new Bitmap(width, height, PixelFormat.Format24bppRgb);

        //    destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

        //    using (var graphics = Graphics.FromImage(destImage))
        //    {
        //        graphics.CompositingMode = CompositingMode.SourceCopy; 
        //        graphics.CompositingQuality = CompositingQuality.HighQuality;
        //        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        //        graphics.SmoothingMode = SmoothingMode.HighQuality;
        //        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        //        using (var wrapMode = new ImageAttributes())
        //        {
        //            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
        //            graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
        //        }
        //    }
        //    return destImage;
        //}
    }
}
