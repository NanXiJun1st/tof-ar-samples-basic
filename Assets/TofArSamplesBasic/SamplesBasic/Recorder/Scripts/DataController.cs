﻿/*
 * SPDX-License-Identifier: (Apache-2.0 OR GPL-2.0-only)
 *
 * Copyright 2023 Sony Semiconductor Solutions Corporation.
 *
 */

using System;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TofArSettings;
using UnityEditor;

namespace TofArSamples.Recorder
{
    public class DataController : ControllerBase
    {
        [SerializeField]
        private AudioClip start;
        [SerializeField]
        private AudioClip stop;
        [SerializeField]
        private string folderName = "TofArData";
        private string savePath;
        public int timer = 0;
        public RecordMode recordMode = RecordMode.Multiple;

        [Header("Save Data")]
        public bool Hand = true;
        public bool Body = true;
        public bool Face = false;
        public bool BlendShape = false;
        public bool Depth = true;
        public bool Color = false;
        public bool Slam = false;

        private List<Recorder> recorders;
        private Coroutine recordCoroutine;
        private string timestampFormat = "yyyyMMdd-HHmmssfff";

        [SerializeField]
        Text txt;
        [SerializeField]
        GameObject panel;
        UnityEngine.Color fontColor;
        int fontSize;
        private AudioSource audioSource;
        public delegate void FinishedRecordBySystemDelegate();
        public FinishedRecordBySystemDelegate finishedRecordDelegate;

        public int[] colorScales
        {
            get
            {
                return new int[] { 1, 2, 4, 10 };
            }
        }

        public int colorScaleIndex
        {
            set
            {
                foreach (var recorder in recorders)
                {
                    if (recorder is ColorRecorder)
                    {
                        ((ColorRecorder)recorder).scale = colorScales[value];
                    }
                }
            }

            get
            {
                var result = 0;
                foreach (var recorder in recorders)
                {
                    if (recorder is ColorRecorder)
                    {
                        result = Array.IndexOf(colorScales, ((ColorRecorder)recorder).scale);
                    }
                }
                return result;
            }
        }

        public int colorSkip
        {
            get
            {
                var result = 0;
                foreach (var recorder in recorders)
                {
                    if (recorder is ColorRecorder)
                    {

                        result = ((ColorRecorder)recorder).skip;
                    }
                }
                return result;
            }

            set
            {
                foreach (var recorder in recorders)
                {
                    if (recorder is ColorRecorder)
                    {
                        ((ColorRecorder)recorder).skip = value;
                    }
                }
            }
        }


        [Header("Option")]
        [SerializeField]
        private float showTime = 0.2f;
        [SerializeField]
        private float fadeOutTime = 0.5f;
        [SerializeField]
        private int timerFontSize = 90;
        [SerializeField]
        private int messageFontSize = 50;

        // 1GB
        [SerializeField]
        private int maxByteSize = 1024 * 1024 * 1024;
        public int MaxByteSize
        {
            get
            {
                return maxByteSize;
            }
        }
        private int totalByteSize = 0;
        public int TotalByteSize
        {
            get
            {
                return totalByteSize;
            }
        }

        private bool showMessage = false;
        private bool fadeMessage = false;
        private string message;
        private float time = 0;

        [SerializeField]
        private DeleteConfirmation dialogPanel;
        private SynchronizationContext context;

        override protected void Start()
        {
            context = SynchronizationContext.Current;

            recorders = GetComponentsInChildren<Recorder>().ToList();
            foreach (var recorder in recorders)
            {
                if (recorder is BinaryRecorder)
                {
                    ((BinaryRecorder)recorder).onDataStored += onDataStored;
                }
            }

            fontColor = txt.color;
            fontSize = txt.fontSize;
            audioSource = GetComponent<AudioSource>();
            panel.SetActive(false);

            dialogPanel.message = "Delete all data?";
            dialogPanel.okAction = DeleteRecord;

            savePath = Application.persistentDataPath + "/" + folderName + "/";
            print("Save Path: " + savePath);

            UpdateRecorders();
        }

        private void Update()
        {
            if (showMessage)
            {
                if (fontColor.a > 0)
                {
                    time += Time.deltaTime;

                    if (fadeMessage)
                    {
                        if (time > showTime)
                        {
                            fontColor.a -= Time.deltaTime / fadeOutTime;
                            txt.color = fontColor;
                        }
                    }
                    else
                    {
                        txt.text = message + new string('.', (int)time % 3 + 1);
                    }
                }
                else
                {
                    showMessage = false;
                    panel.SetActive(false);
                }
            }
        }


        public void StartRecord()
        {
            recordCoroutine = StartCoroutine(StartRecord(timer, true));
        }

        public void StopRecord()
        {
            if (recordMode == RecordMode.Single)
            {
                if (recordCoroutine != null)
                {
                    StopCoroutine(recordCoroutine);
                    ShowMessage("Canceled record.");
                }
            }
            else if (recordMode == RecordMode.Multiple)
            {
                if (recordCoroutine != null)
                {
                    StopCoroutine(recordCoroutine);
                }
                StopRecord(true);
            }
        }

        private void onDataStored(int byteSize)
        {
            totalByteSize += byteSize;
            if (totalByteSize >= maxByteSize)
            {
                context.Post(_ =>
                {
                    StopRecord();
                    finishedRecordDelegate?.Invoke();
                }, null);
            }
        }

        private IEnumerator StartRecord(float TimeRemaining, bool show)
        {
            if (show)
            {
                txt.fontSize = timerFontSize;
            }

            // Wait timer
            while (TimeRemaining > 0)
            {
                yield return null;
                TimeRemaining -= Time.deltaTime;

                if (!show) { continue; }
                int time = Mathf.CeilToInt(TimeRemaining);
                txt.text = time.ToString();
                panel.SetActive(true);

                fontColor.a = TimeRemaining - (time - 1);
                txt.color = fontColor;

                txt.fontSize = (int)(fontSize - (TimeRemaining - time) * 10);
            }

            panel.SetActive(false);

            fontColor.a = 0;
            txt.color = fontColor;

            audioSource.PlayOneShot(start);

            if (recordMode == RecordMode.Single)
            {
                var message = SaveRecord();
                ShowMessage(message, true, true);
                recordCoroutine = null;
                finishedRecordDelegate?.Invoke();
            }
            else
            {
                recorders.ForEach((m) => m.StartRectoring());
            }
        }

        private async Task StopRecord(bool save)
        {
            audioSource.PlayOneShot(stop);
            recorders.ForEach((m) => m.StopRectoring());
            recordCoroutine = null;
            if (!save) { return; }
            ShowMessage("Saving", false, true);
            var message = "";
#if UNITY_EDITOR
            message = SaveRecord();
#else
            message = await Task.Run(() => SaveRecord());
#endif
            ShowMessage(message, true, true);
            totalByteSize = 0;
        }


        private void DeleteRecord()
        {
            if (Directory.Exists(savePath))
            {
                Directory.Delete(savePath, true);
            }

            ShowMessage("Delete all data.");
        }


        private string SaveRecord()
        {
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            var csvCount = 0;
            var binaryCount = 0;

            foreach (var recorder in recorders)
            {
                if (!recorder.Record) { continue; }

                // For csv
                if (recorder is CsvRecorder)
                {
                    var csvRecorder = recorder as CsvRecorder;
                    var path = savePath + csvRecorder.FileName;
                    if (!File.Exists(path))
                    {
                        File.AppendAllText(path, csvRecorder.Header + "\n");
                    }
                    foreach (var data in csvRecorder.GetData(recordMode))
                    {
                        File.AppendAllText(path, string.Format("{0},{1}\n", data.Key.ToString(timestampFormat), data.Value));
                        csvCount++;
                    }
                }
                // For binary
                else if (recorder is BinaryRecorder)
                {
                    var binaryRecorder = recorder as BinaryRecorder;
                    var path = savePath + binaryRecorder.FolderName;
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    foreach (var data in binaryRecorder.GetData(recordMode))
                    {
                        var file = string.Format("{0}/{1}.{2}", path, data.Key.ToString("yyyyMMdd-HHmmssfff"), binaryRecorder.Extension);
                        using (var fs = new FileStream(file, FileMode.Create))
                        {
                            fs.Write(data.Value, 0, data.Value.Length);
                        }
                        binaryCount++;
                    }
                    binaryRecorder.ClearData();
                }
            }

            if (recordMode == RecordMode.Single)
            {
                if (csvCount == 0 && binaryCount == 0)
                {
                    return ("No record saves.");
                }
                else
                {
                    return ("Succeed in save.");
                }
            }
            else if (recordMode == RecordMode.Multiple)
            {
                if (csvCount == 0 && binaryCount == 0)
                {
                    return ("No record saves.");
                }
                else
                {
                    return (string.Format("Succeed in save\n{0} rows, {1} binaries.", csvCount, binaryCount));
                }
            }
            else
            {
                return "";
            }
        }


        private void ShowMessage(string message, bool fade = true, bool guard = false)
        {
            showMessage = true;
            this.message = message;
            fadeMessage = fade;
            txt.text = message;
            fontColor.a = 1;
            txt.color = fontColor;
            txt.fontSize = messageFontSize;
            time = 0;
            panel.SetActive(guard);
        }

        public void UpdateRecorders()
        {
            UpdateRecorder(DataType.Hand, Hand);
            UpdateRecorder(DataType.Body, Body);
            UpdateRecorder(DataType.Face, Face);
            UpdateRecorder(DataType.BlendShape, BlendShape);
            UpdateRecorder(DataType.Depth, Depth);
            UpdateRecorder(DataType.Color, Color);
            UpdateRecorder(DataType.Slam, Slam);
        }

        public void UpdateRecorder(DataType dataType, bool onOff)
        {
            foreach (var recorder in recorders)
            {
                if (recorder.dataType == dataType)
                {
                    recorder.Record = onOff;
                }
            }
        }
    }
}
