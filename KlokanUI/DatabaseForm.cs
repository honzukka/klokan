﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.IO;
using System.Data.SQLite;

namespace KlokanUI
{
	public partial class DatabaseForm : Form
	{
		List<int> yearList;
		List<string> categoryList;

		public DatabaseForm()
		{
			InitializeComponent();

			// initialize the year combo box with a list of years
			yearList = new List<int>();
			for (int year = DateTime.Now.Year; year >= 2000; year--)
			{
				yearList.Add(year);
			}
			yearComboBox.DataSource = yearList;

			// intialize the catgory combo box with a list of categories
			categoryList = new List<string>();
			foreach (var category in Enum.GetValues(typeof(Category)))
			{
				categoryList.Add(category.ToString());
			}
			categoryComboBox.DataSource = categoryList;
		}

		private void PopulateDataView(int year, string category)
		{
			dataView.Rows.Clear();

			using (var db = new KlokanDBContext())
			{
				var instanceQuery = from instance in db.Instances
									where instance.Year == year && instance.Category == category
									select instance;

				KlokanDBInstance currentInstance = instanceQuery.FirstOrDefault();

				// if this instance exits
				if (currentInstance != default(KlokanDBInstance))
				{
					var answerSheetQuery = from sheet in db.AnswerSheets
										   where sheet.Instance.InstanceId == currentInstance.InstanceId
										   orderby sheet.Points descending
										   select new { sheet.AnswerSheetId, sheet.StudentNumber, sheet.Points };

					foreach (var item in answerSheetQuery)
					{
						dataView.Rows.Add(item.AnswerSheetId, item.StudentNumber, item.Points);
					}
				}
			}
		}

		private void viewButton_Click(object sender, EventArgs e)
		{
			// TODO: make this asynchronous!
			PopulateDataView((int)yearComboBox.SelectedItem, (string)categoryComboBox.SelectedItem);
		}

		private void detailButton_Click(object sender, EventArgs e)
		{
			if (dataView.SelectedRows.Count == 0)
			{
				MessageBox.Show("No row has been selected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// multiselect is set to false for this data view
			DatabaseDetailForm form = new DatabaseDetailForm((int)(dataView.SelectedRows[0].Cells[0].Value));
			form.StartPosition = FormStartPosition.CenterScreen;
			form.ShowDialog();
		}

		private void dataView_Click(object sender, EventArgs e)
		{
			if (dataView.SelectedRows.Count == 1)
			{
				detailButton.Enabled = true;
			}
			else
			{
				detailButton.Enabled = false;
			}
		}

		private void importButton_Click(object sender, EventArgs e)
		{
			var dialogResult = openDBDialog.ShowDialog();

			if (dialogResult == DialogResult.OK)
			{
				string importFilePath = openDBDialog.FileName;
				string externalConnectionString = "Data Source=" + importFilePath + ";Pooling=True";
				var externalConnection = new SQLiteConnection(externalConnectionString);

				ImportDB(externalConnection);
			}
		}

		private void exportSelectionButton_Click(object sender, EventArgs e)
		{
			var dialogResult = saveFileDialogExport.ShowDialog();

			if (dialogResult == DialogResult.OK)
			{
				string saveFilePath = saveFileDialogExport.FileName;

				using (var file = new StreamWriter(saveFilePath, false))
				{
					ExportSelection(file, (int)yearComboBox.SelectedItem, (string)categoryComboBox.SelectedItem);
				}
			}
		}

		private void exportAllButton_Click(object sender, EventArgs e)
		{
			var dialogResult = saveFileDialogExport.ShowDialog();

			if (dialogResult == DialogResult.OK)
			{
				string saveFilePath = saveFileDialogExport.FileName;

				using (var file = new StreamWriter(saveFilePath, false))
				{
					ExportAll(file);
				}
			}
		}

		private void ImportDB(SQLiteConnection externalConnection)
		{
			using (var internalDB = new KlokanDBContext())
			using (var externalDB = new KlokanDBContext(externalConnection))
			{
				// load the whole external database
				// (only one instance of a dbcontext can be tracked at a time and this one won't be modified anyway...)
				var externalInstanceQuery = externalDB.Instances
												.Include("AnswerSheets.ChosenAnswers")
												.Include("CorrectAnswers").AsNoTracking();	

				// import new instances
				foreach (var externalInstance in externalInstanceQuery)
				{
					var internalInstanceQuery = from internalInstance in internalDB.Instances
								where internalInstance.Category == externalInstance.Category &&
										internalInstance.Year == externalInstance.Year
								select internalInstance;

					// if the external instance is new, insert it into the internal table
					if (internalInstanceQuery.Count() == 0)
					{
						internalDB.Instances.Add(externalInstance);
					}

					// TODO: new answer sheets can also be imported 
					// but only once a set of columns that differentiates them 
					// is determined (student number + school number???)
				}

				// TODO: asynchronous?
				internalDB.SaveChanges();
			}
		}

		// selects only answer sheets with specific year and category and outputs them
		private void ExportSelection(StreamWriter sw, int year, string category)
		{
			OutputHeader(sw);

			using (var db = new KlokanDBContext())
			{
				// select everything except for scans
				var answerSheetQuery = from answerSheet in db.AnswerSheets
									   where answerSheet.Instance.Year == year && answerSheet.Instance.Category == category
									   select new AnswerSheetSelection
									   {
										   AnswerSheetId = answerSheet.AnswerSheetId,
										   StudentNumber = answerSheet.StudentNumber,
										   Points = answerSheet.Points,
										   Instance = answerSheet.Instance,
										   ChosenAnswers = answerSheet.ChosenAnswers
									   };

				OutputAnswerSheetSelection(sw, answerSheetQuery);
			}
		}

		// selects all answer sheets and outputs them
		private void ExportAll(StreamWriter sw)
		{
			OutputHeader(sw);

			using (var db = new KlokanDBContext())
			{
				// select everything except for scans
				var answerSheetQuery = from answerSheet in db.AnswerSheets
									   select new AnswerSheetSelection {
										   AnswerSheetId = answerSheet.AnswerSheetId,
										   StudentNumber = answerSheet.StudentNumber,
										   Points = answerSheet.Points,
										   Instance = answerSheet.Instance,
										   ChosenAnswers = answerSheet.ChosenAnswers
									   };

				OutputAnswerSheetSelection(sw, answerSheetQuery);
			}
		}

		// outputs answer sheet selection in csv format delimited by a semicolon
		private void OutputAnswerSheetSelection(StreamWriter sw, IQueryable<AnswerSheetSelection> answerSheetSelection)
		{
			foreach (var answerSheet in answerSheetSelection)
			{
				KlokanDBInstance currentInstance = answerSheet.Instance;
				List<KlokanDBChosenAnswer> chosenAnswers = new List<KlokanDBChosenAnswer>(answerSheet.ChosenAnswers);
				List<KlokanDBCorrectAnswer> correctAnswers = new List<KlokanDBCorrectAnswer>(currentInstance.CorrectAnswers);

				sw.Write(answerSheet.AnswerSheetId + ";");
				sw.Write(answerSheet.StudentNumber + ";");
				sw.Write(currentInstance.Year + ";");
				sw.Write(currentInstance.Category + ";");
				sw.Write(answerSheet.Points + ";");

				// relies on the order of answers in the database...
				for (int i = 0; i < 24; i++)
				{
					sw.Write(chosenAnswers[i].Value + ";" + correctAnswers[i].Value + ";");
				}

				sw.WriteLine();
			}
		}

		// outputs a header for the answer sheet selection in csv format delimited by a semicolon
		private void OutputHeader(StreamWriter sw)
		{
			sw.Write("Answer Sheet ID;");
			sw.Write("Student Number;");
			sw.Write("Year;");
			sw.Write("Category;");
			sw.Write("Points;");

			for (int i = 1; i <= 24; i++)
			{
				sw.Write(i + " (Chosen);" + i + " (Correct);");
			}

			sw.WriteLine();
		}

		class AnswerSheetSelection
		{
			public int AnswerSheetId { get; set; }
			public int StudentNumber { get; set; }
			public int Points { get; set; }
			public KlokanDBInstance Instance { get; set; }
			public ICollection<KlokanDBChosenAnswer> ChosenAnswers { get; set; }
		}
	}
}
